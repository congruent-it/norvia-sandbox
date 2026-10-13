#!/usr/bin/env bash
# Post-deploy presenter smoke run of Trip execution (P12.15, not in CI). It runs the demo of the design doc: the Driver's steps
# are the presenter's, in the Driver app, and the Planner's are looked at in the TMS UI; the script stops and says what to do,
# reads the TMS, the Driver app and Application Insights, and checks. Run it in a terminal, signed in to the Azure CLI as the
# presenter the estate was provisioned for, once the Driver reports Logic App is deployed
# (infra/deploy-driver-reports-logic-app.sh) and the Trip dispatch Logic App has delivered the seeded Trip to the Driver app,
# with the number of the Trip the TMS seeded Dispatched for today (the Trip with the seeded Driver in the TMS UI's Trips):
#
#   infra/smoke-test-trip-execution.sh <Trip number>
#
# The demo (user story 25): the Driver signs in as the seeded Driver's Entra test user and starts the Trip, collects all three
# Shipments, delivers the first with a signature and a photo, reports the second as Delivery failed, delivers the third with
# reservations and ends the Trip. It checks that:
#   1. the Trip is Completed in the TMS and the Driver app shows it Completed with every one of the Driver's reports
#      Recorded;
#   2. the first Shipment is Delivered with its Proof of delivery on the timeline, and its Transport Order is Billable;
#   3. the second Shipment is Collected and Awaiting re-delivery, with an open Delivery failed Exception, and is listed in
#      Shipments to put on Trips as a Re-delivery;
#   4. the third Shipment is Delivered with its Proof of delivery and an open Reservations Exception;
#   5. the Trip's reports were processed in order through the session: the Driver reports Logic App recorded them in the
#      order the Driver made them, one after the other;
#   6. the whole path is findable in Application Insights by correlation ID: the Driver app, the Driver reports Logic App
#      and the TMS each logged every correlation ID the Logic App tracked for the Trip, and the Trip number finds the Logic
#      App's and the TMS's rows.
# The Trip is read as the intake identity by a throwaway Container Apps job, as the other smoke runs do. It prints only what
# the checks need: never a signature, a photo, a remark or a token. The presenter's steps are in
# docs/processes/tms/p12-trip-execution.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
trip_number=${1:-}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $trip_number =~ ^TR-[0-9]+$ ]] || fail "usage: $0 <Trip number>, such as TR-1000001"
[ -t 0 ] || fail "the Driver's actions are the presenter's, so run this in a terminal"

fqdn() { az containerapp show --name "$1" --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv; }
tms="https://$(fqdn tms-api)"
tms_ui="https://$(fqdn tms-web)"
driver_app="https://$(fqdn driver-app-api)"
driver_app_ui="https://$(fqdn driver-app-web)"
console="https://$(fqdn integration-console-web)"
logic_app=$AZURE_ESTATE_DRIVER_REPORTS_LOGIC_APP

job="smoke-trip-execution-$(date +%s)"
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  rm -f "$job_definition"
  az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
}
trap cleanup EXIT

workspace=$(az monitor log-analytics workspace show --resource-group "$resource_group" \
  --workspace-name "$AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE" --query customerId --output tsv)
logs() {
  az rest --method post --resource https://api.loganalytics.io --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
    --body "$(jq -n --arg query "$1" '{query: $query, timespan: "P1D"}')" --query 'tables[0].rows' --output json
}
# Ingestion takes a few minutes, so a check that finds nothing asks again, for up to 10 minutes.
until_found() {
  local result
  for _ in $(seq 1 30); do
    result=$("$@")
    [ -z "$result" ] || { echo "$result"; return; }
    sleep 20
  done
}

# The Trip and its Shipments as the TMS has them, read as the intake identity by a throwaway job. The job is described as a
# definition file: `az containerapp job create --args` takes a leading -c for its own flag.
calls='set -eu
az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
TOKEN=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
export TOKEN
python3 - <<"PYTHON"
import json, os, urllib.error, urllib.request

def get(path):
    request = urllib.request.Request(os.environ["TMS"] + path, headers={"Authorization": "Bearer " + os.environ["TOKEN"]})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"httpStatus": error.code}

detail = get("/v1/trips/" + os.environ["TRIP"])
if "httpStatus" in detail:
    print("SMOKE-RESULT " + json.dumps(detail))
    raise SystemExit(0)
open_exceptions = get("/v1/exceptions/open")
to_put_on_trips = get("/v1/shipments/to-put-on-trips")
result = {
    "status": detail["trip"]["status"],
    "driverId": detail["trip"]["driverId"],
    "stops": [{"kind": s["kind"], "shipmentNumber": s["shipmentNumber"], "outcome": s["outcome"]} for s in detail["stops"]],
    "shipments": {},
}
# The Shipments the Trip carried: Stops go when a Shipment is taken off at the end, so the Trip number is read from the Stops
# the Trip kept and from the Shipments the Driver app listed, which the checks name.
for number in os.environ["SHIPMENTS"].split():
    shipment = get("/v1/shipments/" + number)
    timeline = get("/v1/shipments/" + number + "/tracking-events")
    result["shipments"][number] = {
        "status": shipment.get("status"),
        "awaitingRedelivery": shipment.get("awaitingRedelivery"),
        "events": [e["type"] for e in timeline.get("trackingEvents", [])],
        "proofsOfDelivery": len(timeline.get("proofsOfDelivery", [])),
        "billingState": (timeline.get("billing") or {}).get("billingState"),
        "openExceptions": [x["type"] for x in open_exceptions if x["shipmentNumber"] == number] if isinstance(open_exceptions, list) else None,
        "listedAsRedelivery": any(s["shipmentNumber"] == number and s["redelivery"] for s in to_put_on_trips) if isinstance(to_put_on_trips, list) else None,
    }
print("SMOKE-RESULT " + json.dumps(result))
PYTHON'

# Creates the job for the Shipments given, starts it and prints what it read, as one JSON object.
read_tms() {
  jq -n \
    --arg location "$AZURE_LOCATION" \
    --arg identity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
    --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
    --arg calls "$calls" \
    --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
    --arg trip "$trip_number" --arg shipments "$1" '{
      location: $location,
      identity: { type: "UserAssigned", userAssignedIdentities: { ($identity): {} } },
      properties: {
        environmentId: $environment,
        configuration: {
          triggerType: "Manual",
          replicaTimeout: 300,
          replicaRetryLimit: 0,
          manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
        },
        template: {
          containers: [{
            name: "smoke-trip-execution",
            image: "mcr.microsoft.com/azure-cli:latest",
            command: ["/bin/sh", "-c", $calls],
            env: [
              { name: "TMS", value: $tms },
              { name: "TMS_API_CLIENT_ID", value: $tmsApiClientId },
              { name: "INTAKE_CLIENT_ID", value: $intakeClientId },
              { name: "TRIP", value: $trip },
              { name: "SHIPMENTS", value: $shipments }
            ],
            resources: { cpu: 0.5, memory: "1Gi" }
          }]
        }
      }
    }' > "$job_definition"
  az containerapp job create --name "$job" --resource-group "$resource_group" --yaml "$job_definition" --output none
  local execution status
  execution=$(az containerapp job start --name "$job" --resource-group "$resource_group" --query name --output tsv)
  while
    status=$(az containerapp job execution show --name "$job" --resource-group "$resource_group" \
      --job-execution-name "$execution" --query properties.status --output tsv)
    [ "$status" = Running ] || [ "$status" = Processing ]
  do
    sleep 10
  done
  [ "$status" = Succeeded ] || fail "reading the TMS ended $status; see the logs of job execution $execution"
  job_result() {
    logs "ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$job' and Log_s has 'SMOKE-RESULT ' and TimeGenerated > ago(15m)
| order by TimeGenerated desc | project Log_s" | jq -r '.[0][0] // empty' | sed 's/^SMOKE-RESULT //'
  }
  local result
  result=$(until_found job_result)
  [ -n "$result" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
  echo "$result"
}

# The Driver's Trip as the Driver app holds it.
driver_id=$(jq -r '.demoTripExecution.driverId' "$(cd "$(dirname "$0")/.." && pwd)/seed/norvia.seed.json")
driver_trip() {
  curl -fsS "$driver_app/v1/drivers/$driver_id/trips" | jq -c --arg trip "$trip_number" '.[] | select(.tripNumber == $trip)'
}

# The Logic App's rows for the Trip's reports (ADR 0016): when each report was recorded, by its Driver app event ID.
recorded_order() {
  logs "union AppTraces, AppDependencies, AppRequests
| where tostring(Properties.flow) == 'trip-execution' and tostring(Properties.tripNumber) == '$trip_number'
    and tostring(Properties.outcome) == 'Recorded' and isnotempty(tostring(Properties.sourceMessageId))
| summarize recordedAt = min(TimeGenerated) by sourceMessageId = tolower(tostring(Properties.sourceMessageId))
| order by recordedAt asc" | jq -r '.[][0]'
}
# The correlation IDs the Logic App tracked for the Trip: one for each of the Driver's actions.
logic_app_correlation_ids() {
  logs "union AppTraces, AppDependencies, AppRequests
| where tostring(Properties.flow) == 'trip-execution' and tostring(Properties.tripNumber) == '$trip_number' and isnotempty(tostring(Properties.correlationId))
| distinct tostring(Properties.correlationId)" | jq -r '.[][0]'
}

# Application Insights: the roles whose rows carry a value.
roles_with() {
  logs "union AppRequests, AppTraces, AppDependencies
| where tostring(Properties.correlationId) == '$1' or OperationId == '$1' or tostring(Properties.tripNumber) == '$1'
| summarize by AppRoleName" | jq -r '.[][0]'
}
found() {
  local what=$1 value=$2 roles missing
  shift 2
  for _ in $(seq 1 30); do
    roles=$(roles_with "$value")
    missing=
    for role in "$@"; do
      grep -qi "$role" <<<"$roles" || missing="$missing $role"
    done
    if [ -z "$missing" ]; then
      pass "$what is in Application Insights: $(tr '\n' ' ' <<<"$roles")"
      return
    fi
    sleep 20
  done
  fail "$what isn't in Application Insights for$missing after 10 minutes"
}

pause() { read -r -p "Press Enter when done. " _; }

echo "Trip execution: Trip $trip_number, Driver $driver_id"
# The Trip must be for today, in Norvia's time: Start Trip is refused on any other day (NotTripDay). The seed puts it on the day the
# TMS first started (the next Monday on a weekend), so a TMS seeded days ago, or at a weekend, needs a fresh TMS database first.
trip=$(driver_trip)
[ -n "$trip" ] || fail "the Driver app doesn't hold Trip $trip_number for Driver $driver_id: has the Trip dispatch Logic App delivered it, and is $driver_id the Trip's Driver?"
today=$(TZ=Europe/Amsterdam date +%F)
[ "$(jq -r '.day' <<<"$trip")" = "$today" ] \
  || fail "Trip $trip_number is for $(jq -r '.day' <<<"$trip"), not today ($today), so Start Trip would be refused with NotTripDay; seed a fresh TMS database and run this on a weekday"
cat <<EOF
As the Driver in the Driver app ($driver_app_ui), signed in as the seeded Driver's Entra test user (the presenter panel
has the sign-in details). Open the Trip of the day, with three Shipments, and:
  1. Start the Trip. As the Planner in the TMS UI ($tms_ui/trips/$trip_number) see it go In progress.
  2. Collect all three Shipments.
  3. Deliver the first with a signature and a photo. The Planner sees it Delivered with its Proof of delivery on the
     timeline, and its Transport Order Billable.
  4. Report the second as Delivery failed, with a remark. The Planner sees a Delivery failed Exception.
  5. Deliver the third with reservations (and a remark), a signature and no photo. The Planner sees a Reservations Exception.
  6. End the Trip. The second Shipment is in Shipments to put on Trips ($tms_ui/trips) as a Re-delivery.
Wait for every report on the Trip card to show Recorded before pressing Enter.
EOF
pause

# 1. The Driver app's side: the Trip Completed and every report Recorded.
trip=$(driver_trip)
[ -n "$trip" ] || fail "the Driver app doesn't hold Trip $trip_number for Driver $driver_id"
jq -e '.status == "Completed" and (.reports | length > 0) and all(.reports[]; .state == "Recorded")' <<<"$trip" >/dev/null \
  || fail "the Driver app doesn't show Trip $trip_number Completed with every report Recorded: $(jq -c '{status, reports: [.reports[] | {kind, state, failureCode}]}' <<<"$trip")"
jq -e '[.reports[].kind] as $kinds | ($kinds | first) == "StartTrip" and ($kinds | last) == "EndTrip"
  and ([$kinds[] | select(. == "Collected")] | length) == 3 and ([$kinds[] | select(. == "Delivered")] | length) == 2
  and ([$kinds[] | select(. == "DeliveryFailed")] | length) == 1' <<<"$trip" >/dev/null \
  || fail "the Driver's reports on Trip $trip_number aren't the demo's (Start, three Collected, two Delivered, one Delivery failed, End): $(jq -c '[.reports[].kind]' <<<"$trip")"
shipments=$(jq -r '[.reports[].shipmentNumber | select(. != null)] | unique | join(" ")' <<<"$trip")

state=$(read_tms "$shipments")
jq -e '.status == "Completed"' <<<"$state" >/dev/null || fail "the TMS doesn't have Trip $trip_number Completed: $(jq -c '{status, httpStatus}' <<<"$state")"
pass "Trip $trip_number is Completed in the TMS, and the Driver app shows it Completed with all $(jq '.reports | length' <<<"$trip") reports Recorded"

# 2 to 4. The Shipments, by what became of each.
delivered=$(jq -r '.shipments | to_entries[] | select(.value.status == "Delivered") | .key' <<<"$state")
failed=$(jq -r '.shipments | to_entries[] | select(.value.awaitingRedelivery == true) | .key' <<<"$state")
[ "$(wc -w <<<"$delivered")" -eq 2 ] && [ "$(wc -w <<<"$failed")" -eq 1 ] \
  || fail "two Shipments should be Delivered and one Awaiting re-delivery: $(jq -c '.shipments | map_values({status, awaitingRedelivery})' <<<"$state")"
jq -e --arg s "$failed" '.shipments[$s] | .status == "Collected" and (.openExceptions | index("DeliveryFailed")) != null and .listedAsRedelivery == true' <<<"$state" >/dev/null \
  || fail "the Shipment that failed ($failed) isn't Collected with an open Delivery failed Exception and listed in Shipments to put on Trips as a Re-delivery: $(jq -c --arg s "$failed" '.shipments[$s]' <<<"$state")"
pass "$failed is Collected and Awaiting re-delivery, with a Delivery failed Exception open, and listed in Shipments to put on Trips as a Re-delivery"
billable=0 reservations=0
for shipment in $delivered; do
  jq -e --arg s "$shipment" '.shipments[$s] | .proofsOfDelivery >= 1 and (.events | index("Delivered")) != null' <<<"$state" >/dev/null \
    || fail "$shipment isn't Delivered with a Proof of delivery on its timeline: $(jq -c --arg s "$shipment" '.shipments[$s]' <<<"$state")"
  jq -e --arg s "$shipment" '.shipments[$s].billingState == "Billable"' <<<"$state" >/dev/null && billable=$((billable + 1))
  jq -e --arg s "$shipment" '.shipments[$s].openExceptions | index("Reservations")' <<<"$state" >/dev/null && reservations=$((reservations + 1))
done
[ "$billable" -ge 1 ] && [ "$reservations" -eq 1 ] \
  || fail "a Delivered Shipment should be Billable (the clean one) and exactly one should have a Reservations Exception, found $billable Billable and $reservations with Reservations: $(jq -c '.shipments' <<<"$state")"
pass "the two Delivered Shipments have their Proof of delivery on the timeline; $billable Billable, and one has a Reservations Exception open"

# 5. In order through the session: the Logic App recorded the reports in the order the Driver made them. The session (keyed by Trip
# number) is what makes that so, but it is seen only in its result: a report with no 'Recorded' row of the Logic App, or one
# recorded out of turn, fails this check.
expected=$(jq -r '.reports[].id | ascii_downcase' <<<"$trip") # oldest first
actual=$(until_found recorded_order)
[ -n "$actual" ] || fail "no recorded report of Trip $trip_number reached Application Insights after 10 minutes"
[ "$actual" = "$expected" ] \
  || fail "the Driver reports Logic App recorded Trip $trip_number's reports out of order. Made: $(tr '\n' ' ' <<<"$expected") Recorded: $(tr '\n' ' ' <<<"$actual")"
pass "the Logic App recorded Trip $trip_number's $(wc -l <<<"$actual") reports in the order the Driver made them"

# 6. The whole path, by Trip number and by correlation ID.
echo
found "Trip $trip_number, by its number," "$trip_number" "$logic_app" tms-api
ids=$(until_found logic_app_correlation_ids)
[ -n "$ids" ] || fail "no correlation ID of Trip $trip_number reached Application Insights after 10 minutes"
while read -r id; do
  found "Trip $trip_number, by correlation ID $id," "$id" driver-app "$logic_app" tms-api
done <<<"$ids"

echo
echo "Open in the TMS UI:"
echo "  Trip detail:       $tms_ui/trips/$trip_number"
echo "  Shipments to put on Trips: $tms_ui/trips"
echo "Open in the Integration console, one row per hop:"
echo "  Search:            $console/?key=$trip_number"
