#!/usr/bin/env bash
# Post-deploy presenter smoke run of Trip dispatch (P11.14, not in CI). Two scenarios. The Planner's actions and the
# presenter panel's switch are the presenter's, in the TMS UI and the Partner portal; the script stops and says what to do,
# reads the TMS, the Driver app and Application Insights, and checks. Run it in a terminal, signed in to the Azure CLI as
# the presenter the estate was provisioned for, once the demo's Shipments are seeded and the Trip dispatch Logic App is
# deployed (infra/deploy-trip-dispatch-logic-app.sh), with the numbers of the two Trips the presenter builds:
#
#   infra/smoke-test-trip-dispatch.sh <scenario 1 Trip number> <scenario 2 Trip number>
#
# Scenario 1 (the presenter, as the Planner): build a Trip from the seeded Shipments, add the one that is too heavy for the
# van and see PayloadExceeded, dispatch the Trip, wait for Delivered to Driver app, take a Shipment off and wait for version
# 2 to be Delivered. Scenario 2: switch "Driver app unavailable" on in the Partner portal's presenter panel, build and
# dispatch a Trip, see Not delivered, switch the fault off and send the version again.
# It checks that:
#   1. scenario 1: the Trip is Dispatched with versions 1 and 2, both Delivered to Driver app, and the Driver app holds
#      version 2 for the Trip's Driver, with the Stops the TMS has after the take-off;
#   2. scenario 2, with the fault on: the Trip's version is Not delivered with a cause, the Driver app does not hold it,
#      and the Trip dispatch Logic App's send failed with DriverAppUnavailable;
#   3. scenario 2, after the fault is off and the version sent again: the same version is Delivered to Driver app and the
#      Driver app holds it, and the fault is off;
#   4. the whole path is findable in Application Insights by correlation ID: the TMS, the Trip dispatch Logic App and the
#      Driver app each logged the Trip's dispatch, and the Trip number finds the Logic App's rows.
# The TMS's queries answer only a principal holding one of its roles, so a throwaway Container Apps job reads them as the
# intake identity, as the other smoke runs do. It prints only what the checks need. The presenter's steps are in
# docs/processes/tms/p11-trip-dispatch.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
scenario1=${1:-}
scenario2=${2:-}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $scenario1 =~ ^TR-[0-9]+$ && $scenario2 =~ ^TR-[0-9]+$ && $scenario1 != "$scenario2" ]] \
  || fail "usage: $0 <scenario 1 Trip number> <scenario 2 Trip number>, such as TR-1000001 TR-1000002"
[ -t 0 ] || fail "the Planner's actions are the presenter's, so run this in a terminal"

fqdn() { az containerapp show --name "$1" --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv; }
tms="https://$(fqdn tms-api)"
tms_ui="https://$(fqdn tms-web)"
portal="https://$(fqdn partner-portal-web)"
driver_app="https://$(fqdn driver-app-api)"
driver_app_ui="https://$(fqdn driver-app-web)"
console="https://$(fqdn integration-console-web)"
logic_app=$AZURE_ESTATE_TRIP_DISPATCH_LOGIC_APP

job="smoke-trip-dispatch-$(date +%s)"
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

# The Trips as the TMS has them, read as the intake identity by a throwaway job. The job is described as a definition file:
# `az containerapp job create --args` takes a leading -c for its own flag.
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

result = {}
for number in os.environ["NUMBERS"].split():
    detail = get("/v1/trips/" + number)
    # Only what the checks need: no Stop holds anything of a Consignee here, but the checks need no more than this.
    result[number] = detail if "httpStatus" in detail else {
        "status": detail["trip"]["status"],
        "driverId": detail["trip"]["driverId"],
        "stopCount": len(detail["stops"]),
        "versions": [{"version": v["version"], "state": v["state"], "cause": v["cause"]} for v in detail["versions"]],
    }
print("SMOKE-RESULT " + json.dumps(result))
PYTHON'
jq -n \
  --arg location "$AZURE_LOCATION" \
  --arg identity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
  --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
  --arg calls "$calls" \
  --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
  --arg numbers "$scenario1 $scenario2" '{
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
          name: "smoke-trip-dispatch",
          image: "mcr.microsoft.com/azure-cli:latest",
          command: ["/bin/sh", "-c", $calls],
          env: [
            { name: "TMS", value: $tms },
            { name: "TMS_API_CLIENT_ID", value: $tmsApiClientId },
            { name: "INTAKE_CLIENT_ID", value: $intakeClientId },
            { name: "NUMBERS", value: $numbers }
          ],
          resources: { cpu: 0.5, memory: "1Gi" }
        }]
      }
    }
  }' > "$job_definition"
az containerapp job create --name "$job" --resource-group "$resource_group" --yaml "$job_definition" --output none

# Starts the job and prints the Trips it read, as one JSON object by Trip number.
read_trips() {
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

# What the Driver app holds for a Driver, read from its API: the Trip's version, or nothing.
driver_app_version() {
  curl -fsS "$driver_app/v1/drivers/$1/trips" | jq -r --arg trip "$2" '.[] | select(.tripNumber == $trip) | .version'
}
driver_app_stops() {
  curl -fsS "$driver_app/v1/drivers/$1/trips" | jq -r --arg trip "$2" '.[] | select(.tripNumber == $trip) | .stops | length'
}
trip_of() { jq -c --arg trip "$2" '.[$trip]' <<<"$1"; }

# The send to the Driver app that the Trip dispatch Logic App tracked for a Trip, by the failure code it recorded.
send_failure_codes() {
  logs "union AppTraces, AppDependencies, AppRequests
| where tostring(Properties.flow) == 'trip-dispatch' and tostring(Properties.tripNumber) == '$1'
    and tostring(Properties.outcome) == 'Failed' and isnotempty(tostring(Properties.failureCode))
| distinct tostring(Properties.failureCode)" | jq -r '.[][0]'
}
has_send_failure() { send_failure_codes "$1" | grep -qx "$2" && echo yes || true; }

# Application Insights: the roles whose rows carry a value.
roles_with() {
  logs "union AppRequests, AppTraces, AppDependencies
| where tostring(Properties.correlationId) == '$1' or OperationId == '$1'
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
# The correlation IDs the Trip dispatch Logic App tracked for a Trip: each is one Dispatch, Amend or send again.
logic_app_correlation_ids() {
  logs "union AppTraces, AppDependencies, AppRequests
| where tostring(Properties.flow) == 'trip-dispatch' and tostring(Properties.tripNumber) == '$1' and isnotempty(tostring(Properties.correlationId))
| distinct tostring(Properties.correlationId)" | jq -r '.[][0]'
}

pause() { read -r -p "Press Enter when done. " _; }

# Scenario 1.
echo
echo "Scenario 1: build, dispatch and amend"
cat <<EOF
As the Planner in the TMS UI ($tms_ui/trips):
  1. Build Trip $scenario1 from the seeded Shipments of the demo's day, on the van: select the two that fit a van and the
     one too heavy for it, and see PayloadExceeded when the heavy one is added. Add the two that fit instead.
  2. Pick a Vehicle, a Driver, a planned start and a driving duration, and Dispatch the Trip.
  3. Open the Driver app ($driver_app_ui), pick the Trip's Driver, and see the Trip there.
  4. On the Trip's detail take one Shipment off (give a reason) and wait for version 2 to show Delivered to Driver app.
EOF
pause
trips=$(read_trips)
trip=$(trip_of "$trips" "$scenario1")
jq -e 'type == "object" and (.httpStatus | not)' <<<"$trip" >/dev/null \
  || fail "the TMS answered $(jq -r '.httpStatus // "nothing"' <<<"$trip") for Trip $scenario1"
jq -e '.status == "Dispatched" and (.versions | length == 2) and all(.versions[]; .state == "Delivered")
  and .versions[0].version == 1 and .versions[1].version == 2' <<<"$trip" >/dev/null \
  || fail "Trip $scenario1 isn't Dispatched with versions 1 and 2 Delivered to Driver app: $trip"
driver1=$(jq -r '.driverId' <<<"$trip")
[ "$(driver_app_version "$driver1" "$scenario1")" = 2 ] \
  || fail "the Driver app doesn't hold version 2 of Trip $scenario1 for Driver $driver1"
pass "Trip $scenario1 is Dispatched, versions 1 and 2 are Delivered to Driver app, and the Driver app holds version 2 for Driver $driver1"
[ "$(driver_app_stops "$driver1" "$scenario1")" = "$(jq -r '.stopCount' <<<"$trip")" ] \
  || fail "the Driver app's Stops of Trip $scenario1 differ from the TMS's $(jq -r '.stopCount' <<<"$trip")"
pass "the Driver app's version 2 has the same $(jq -r '.stopCount' <<<"$trip") Stops as the TMS, after the take-off"

# Scenario 2.
echo
echo "Scenario 2: the Driver app unavailable"
cat <<EOF
  1. Open the presenter panel ($portal/presenter) and switch on the fault driver-app-unavailable. It switches itself off after the
     duration you pick (two minutes by default), so build and dispatch the Trip within that time.
  2. As the Planner, build Trip $scenario2 from the Shipments that are left (the one that was taken off, and the rest), with
     the other Vehicle and Driver, and Dispatch it.
  3. Wait for the Trip to show Not delivered (the Logic App retries for about a minute first).
EOF
pause
# The fault switch asks for the Demo.Fault role, which only the portal's identity holds, so the script doesn't read it: the Trip below being
# Not delivered, and the Driver app not holding it, show the fault was on.
trips=$(read_trips)
trip=$(trip_of "$trips" "$scenario2")
jq -e 'type == "object" and (.httpStatus | not)' <<<"$trip" >/dev/null \
  || fail "the TMS answered $(jq -r '.httpStatus // "nothing"' <<<"$trip") for Trip $scenario2"
jq -e '.status == "Dispatched" and (.versions | length >= 1) and .versions[-1].state == "NotDelivered" and (.versions[-1].cause | length > 0)' \
  <<<"$trip" >/dev/null \
  || fail "Trip $scenario2's latest version isn't Not delivered with a cause: $trip"
sent_version=$(jq -r '.versions[-1].version' <<<"$trip")
driver2=$(jq -r '.driverId' <<<"$trip")
[ -z "$(driver_app_version "$driver2" "$scenario2")" ] \
  || fail "the Driver app holds Trip $scenario2 although it was unavailable"
pass "Trip $scenario2 version $sent_version is Not delivered ($(jq -r '.versions[-1].cause' <<<"$trip")) and the Driver app doesn't hold it"
[ -n "$(until_found has_send_failure "$scenario2" DriverAppUnavailable)" ] \
  || fail "no failed send with DriverAppUnavailable for Trip $scenario2 reached Application Insights after 10 minutes"
pass "the Trip dispatch Logic App's send for Trip $scenario2 failed with DriverAppUnavailable"
echo
echo "Open the failed run of the $logic_app Logic App in the Integration console: $console/?key=$scenario2"
cat <<EOF
  4. Switch the fault off in the presenter panel ($portal/presenter).
  5. On Trip $scenario2's detail send version $sent_version again, and wait for it to show Delivered to Driver app.
EOF
pause
trips=$(read_trips)
trip=$(trip_of "$trips" "$scenario2")
jq -e --argjson version "$sent_version" '(.versions | length) == $version and .versions[-1].version == $version and .versions[-1].state == "Delivered"' \
  <<<"$trip" >/dev/null \
  || fail "Trip $scenario2's version $sent_version isn't Delivered to Driver app after sending it again: $trip"
[ "$(driver_app_version "$driver2" "$scenario2")" = "$sent_version" ] \
  || fail "the Driver app doesn't hold version $sent_version of Trip $scenario2 for Driver $driver2"
pass "version $sent_version of Trip $scenario2 was sent again and is Delivered to Driver app, so the fault is off"

# 4. The whole path, by correlation ID and by Trip number.
echo
for trip_number in "$scenario1" "$scenario2"; do
  ids=$(until_found logic_app_correlation_ids "$trip_number")
  [ -n "$ids" ] || fail "no correlation ID of Trip $trip_number reached Application Insights after 10 minutes"
  found "Trip $trip_number, by its number," "$trip_number" "$logic_app" tms-api
  while read -r id; do
    found "Trip $trip_number, by correlation ID $id," "$id" tms-api "$logic_app" driver-app-api
  done <<<"$ids"
done

echo
echo "Open in the TMS UI:"
echo "  Trips:             $tms_ui/trips"
echo "  Trip detail:       $tms_ui/trips/$scenario1  and  $tms_ui/trips/$scenario2"
echo "Open in the Integration console, one row per hop:"
echo "  Search:            $console/?key=$scenario1  and  $console/?key=$scenario2"
