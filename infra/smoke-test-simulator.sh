#!/usr/bin/env bash
# Post-deploy presenter smoke run of the Simulator (not in CI). The presenter signs in to the Simulator and runs a small
# simulation, which the script can't do for them: Entra signs them in, and the Simulator's screens are theirs. The script
# stops and says what to do, reads the TMS and Application Insights, and checks. Run it in a terminal, signed in to the
# Azure CLI as the presenter the estate was provisioned for, once the estate's Logic Apps are deployed (azd up does it):
#
#   infra/smoke-test-simulator.sh <run number>
#
# The run number is the one the Simulator's Configure screen says the next run gets; the script asks you to read it before
# you start the run. The presenter's steps (docs/operations/simulator.md, "Presenter smoke run in Azure"): choose Quiet
# Monday, set the seed to 349, start the run and wait until it is Completed. The script then checks that:
#   1. the Simulator is up, and Easy Auth stands in front of it: its health check answers, and any other request from a caller
#      without a sign-in is sent to Entra;
#   2. the Transport Orders the run booked are in the TMS under their Shipper references SIM-<run>-<n>, most of them
#      Planned, and some Rejected (the bad bookings Norvia refuses);
#   3. the Planner's Shipments are in the TMS: released, in progress or put on Trips;
#   4. at least one Trip carries a Stop for one of the run's Transport Orders;
#   5. the paths are findable in Application Insights by correlation ID: a booking's correlation ID is found in the
#      Simulator, the intake Logic App and the TMS, and a Planner action's correlation ID in the Simulator and the TMS.
# The TMS is read as the intake identity by a throwaway Container Apps job, as the other smoke runs do. It prints numbers and
# identifiers only: never a Consignee, an address or a token.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
run_number=${1:-}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $run_number =~ ^[0-9]+$ ]] || fail "usage: $0 <run number>, such as 1: the number the Configure screen gave the run"
[ -t 0 ] || fail "the presenter's steps are theirs, so run this in a terminal"

fqdn() { az containerapp show --name "$1" --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv; }
simulator="https://$(fqdn simulator-web)"
tms="https://$(fqdn tms-api)"
tms_ui="https://$(fqdn tms-web)"
console="https://$(fqdn integration-console-web)"
intake_logic_app=$AZURE_ESTATE_INTAKE_LOGIC_APP
reference_prefix="SIM-$run_number-"

job="smoke-simulator-$(date +%s)"
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

# 1. The Simulator is up, and Easy Auth stands in front of it.
health=$(curl -s -o /dev/null -w '%{http_code}' "$simulator/health" || true)
[ "$health" = 200 ] || fail "the Simulator's health check answered $health, not 200: is $simulator deployed, and is its migration job done?"
unsigned=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' "$simulator/" || true)
[[ $unsigned == "302 https://login.microsoftonline.com/"* ]] \
  || fail "a request to the Simulator without a sign-in should be sent to Entra, and got: ${unsigned%% *}"
pass "the Simulator is up, and a caller without a sign-in is sent to Entra"

cat <<EOF2
As the presenter, in a browser, open the Simulator ($simulator) and sign in with Entra. Someone without the Simulator.Run and
Simulator.Read roles is refused by Entra, so you need both (the estate assigned them to you). Then:
  1. On Configure, check the number the next run gets is $run_number.
  2. Choose Quiet Monday, set the seed to 349 and Start run.
  3. Watch the log fill. Wait until the run is Completed (Cancelled or Failed is not a pass).
EOF2
read -r -p "Press Enter when the run is Completed. " _

# 2 to 4. The TMS, as the intake identity: the run's Transport Orders, their Shipments and the Trips. The job is described as a
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
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"httpStatus": error.code}

prefix = os.environ["PREFIX"]
orders = {}
for status in ("Accepted", "Planned", "Rejected"):
    listed = get("/v1/transport-orders?status=" + status)
    if isinstance(listed, dict):
        print("SMOKE-RESULT " + json.dumps({"httpStatus": listed["httpStatus"], "reading": "transport orders " + status}))
        raise SystemExit(0)
    orders[status] = [o for o in listed if (o.get("shipperReference") or "").startswith(prefix)]
numbers = {o["transportOrderNumber"] for status in orders.values() for o in status if o.get("transportOrderNumber")}

shipments = 0
for path in ("/v1/shipments/to-release", "/v1/shipments/to-put-on-trips"):
    listed = get(path)
    if isinstance(listed, list):
        shipments += sum(1 for s in listed if s.get("transportOrderNumber") in numbers)

trips_with_run_stops = 0
shipment_numbers_on_trips = set()
trips = get("/v1/trips")
for trip in trips if isinstance(trips, list) else []:
    detail = get("/v1/trips/" + trip["number"])
    stops = detail.get("stops", []) if isinstance(detail, dict) else []
    mine = [s for s in stops if s.get("transportOrderNumber") in numbers]
    if mine:
        trips_with_run_stops += 1
        shipment_numbers_on_trips.update(s["shipmentNumber"] for s in mine)

in_progress = get("/v1/shipments/in-progress")
in_progress_count = len(in_progress) if isinstance(in_progress, list) else 0
print("SMOKE-RESULT " + json.dumps({
    "accepted": len(orders["Accepted"]), "planned": len(orders["Planned"]), "rejected": len(orders["Rejected"]),
    "shipmentsWaiting": shipments, "shipmentsOnTrips": len(shipment_numbers_on_trips), "tripsWithRunStops": trips_with_run_stops,
    "shipmentsInProgress": in_progress_count,
}))
PYTHON'

jq -n \
  --arg location "$AZURE_LOCATION" \
  --arg identity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
  --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
  --arg calls "$calls" \
  --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
  --arg prefix "$reference_prefix" '{
    location: $location,
    identity: { type: "UserAssigned", userAssignedIdentities: { ($identity): {} } },
    properties: {
      environmentId: $environment,
      configuration: {
        triggerType: "Manual",
        replicaTimeout: 600,
        replicaRetryLimit: 0,
        manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      },
      template: {
        containers: [{
          name: "smoke-simulator",
          image: "mcr.microsoft.com/azure-cli:latest",
          command: ["/bin/sh", "-c", $calls],
          env: [
            { name: "TMS", value: $tms },
            { name: "TMS_API_CLIENT_ID", value: $tmsApiClientId },
            { name: "INTAKE_CLIENT_ID", value: $intakeClientId },
            { name: "PREFIX", value: $prefix }
          ],
          resources: { cpu: 0.5, memory: "1Gi" }
        }]
      }
    }
  }' > "$job_definition"
az containerapp job create --name "$job" --resource-group "$resource_group" --yaml "$job_definition" --output none
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
state=$(until_found job_result)
[ -n "$state" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
jq -e 'has("httpStatus") | not' <<<"$state" >/dev/null || fail "the TMS refused reading the run: $(jq -c . <<<"$state")"

jq -e '.planned >= 1 and (.planned + .accepted) >= 1' <<<"$state" >/dev/null \
  || fail "no Transport Order of run $run_number ($reference_prefix...) is Planned in the TMS: $(jq -c . <<<"$state")"
pass "run $run_number booked $(jq '.planned + .accepted + .rejected' <<<"$state") Transport Orders in the TMS: $(jq '.planned' <<<"$state") Planned, $(jq '.accepted' <<<"$state") Accepted, $(jq '.rejected' <<<"$state") Rejected"
jq -e '(.shipmentsWaiting + .shipmentsOnTrips + .shipmentsInProgress) >= 1' <<<"$state" >/dev/null \
  || fail "none of the run's Planned orders has a Shipment the TMS shows to release, on a Trip or in progress: $(jq -c . <<<"$state")"
pass "the Planner's Shipments are in the TMS: $(jq '.shipmentsWaiting' <<<"$state") waiting to be released or put on Trips, $(jq '.shipmentsOnTrips' <<<"$state") on Trips"
jq -e '.tripsWithRunStops >= 1' <<<"$state" >/dev/null \
  || fail "no Trip in the TMS carries a Stop for the run's Transport Orders: did the run use a preset that puts Shipments on Trips, and was it Completed?"
pass "$(jq '.tripsWithRunStops' <<<"$state") Trip(s) in the TMS carry Stops for the run's Transport Orders"

# 5. The paths in Application Insights, by correlation ID: the ones the Simulator logged for the run (its tracking keys).
simulator_correlation_id() {
  logs "union AppTraces, AppDependencies, AppRequests
| where TimeGenerated > ago(1d) and tostring(Properties.flow) == '$1' and tostring(Properties.outcome) == 'Succeeded'
    and isnotempty(tostring(Properties.correlationId))
| order by TimeGenerated desc | take 1 | project tostring(Properties.correlationId)" | jq -r '.[0][0] // empty'
}
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

echo
booking_id=$(until_found simulator_correlation_id transport-order-intake)
[ -n "$booking_id" ] || fail "no booking the Simulator logged as succeeded reached Application Insights in 10 minutes"
found "A booking, by correlation ID $booking_id," "$booking_id" simulator "$intake_logic_app" tms-api
planner_id=$(until_found simulator_correlation_id transport-planning)
[ -n "$planner_id" ] || fail "no Planner action the Simulator logged as succeeded reached Application Insights in 10 minutes"
found "A Planner action, by correlation ID $planner_id," "$planner_id" simulator tms-api

echo
echo "Open in the Integration console, one row per hop:"
echo "  A booking:         $console/?key=$booking_id"
echo "  A Planner action:  $console/?key=$planner_id"
echo "Open in the TMS UI, searching for $reference_prefix:"
echo "  Transport Orders:  $tms_ui/"
echo "  Trips:             $tms_ui/trips"
