#!/usr/bin/env bash
# Post-deploy presenter smoke run of Transport planning (P3.7, not in CI). Book Rotterdam to Düsseldorf as Brouwer
# Dranken in the Partner portal first, then as the Planner in the TMS UI assign it to Van Dijk Transport, reassign it
# to Kessler Spedition and release it, then run it, signed in to the Azure CLI as the presenter the estate was
# provisioned for, with the Transport Order number the portal showed:
#
#   infra/smoke-test-transport-planning.sh <Transport Order number>
#
# It checks that:
#   1. the Transport Order is Planned, with its Shipment;
#   2. the Shipment is Released, carried by Kessler Spedition, and its history of assignments shows it was first
#      planned to Van Dijk Transport and then reassigned to Kessler Spedition;
#   3. it has left both the Planner's list of Transport Orders to plan and their list of Shipments to release;
#   4. the assignment, the reassignment and the release each ran on a Message trail of its own, under the Transport
#      Order number, publishing ShipmentPlanned, ShipmentReassigned and ShipmentReleased on the Shipment events
#      topic in turn.
# The TMS's queries answer only a principal holding one of its roles, so a throwaway Container Apps job reads them as
# the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a
# body, as the Transport Order holds the Consignee's personal data. It ends with the TMS UI's and the Integration
# console's pages to open. The presenter's steps are in docs/processes/tms/p3-transport-planning.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
number=${1:-}
brouwer_dranken_id=SHP-0001
van_dijk_id=CAR-0001
kessler_id=CAR-0002

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $number =~ ^TO-[0-9]+$ ]] \
  || fail "usage: $0 <Transport Order number>, such as TO-0000042"

tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
echo "TMS: $tms"
echo "Transport Order: $number"

job="smoke-planning-$(date +%s)"
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  rm -f "$job_definition"
  az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
}
trap cleanup EXIT

# 1 to 3. The TMS's answers, as the intake identity. The job is described as a definition file: `az containerapp job
# create --args` takes a leading -c for its own flag.
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

def carried_by(view):
    carrier = view.get("carrier")
    return {
        "assignedTo": view["assignedTo"],
        "carrierId": None if carrier is None else carrier["id"],
        "carrierName": None if carrier is None else carrier["name"],
    }

number = os.environ["NUMBER"]
order = get("/v1/transport-orders/" + number)
# Only what the checks need: the Transport Order and the Shipment hold the Consignee'"'"'s personal data.
result = {"transportOrder": order if "httpStatus" in order else {
    "status": order["status"],
    "shipmentNumber": order["shipmentNumber"],
    "shipperId": order["shipper"]["id"],
}}

shipment_number = result["transportOrder"].get("shipmentNumber")
if shipment_number:
    shipment = get("/v1/shipments/" + shipment_number)
    result["shipment"] = shipment if "httpStatus" in shipment else {
        "status": shipment["status"],
        "shipperId": shipment["shipper"]["id"],
        "correlationId": shipment["correlationId"],
        **carried_by(shipment["carriedBy"]),
        "assignments": [{**carried_by(a["carriedBy"]), "correlationId": a["correlationId"]} for a in shipment["assignments"]],
    }

waiting = get("/v1/transport-orders?status=Accepted")
to_release = get("/v1/shipments/to-release")
result["waitingForPlanning"] = waiting if not isinstance(waiting, list) else any(w["transportOrderNumber"] == number for w in waiting)
result["toRelease"] = to_release if not isinstance(to_release, list) else (
    shipment_number is not None and any(s["shipmentNumber"] == shipment_number for s in to_release))
print("SMOKE-RESULT " + json.dumps(result))
PYTHON'
jq -n \
  --arg location "$AZURE_LOCATION" \
  --arg identity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
  --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
  --arg calls "$calls" \
  --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
  --arg number "$number" '{
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
          name: "smoke-planning",
          image: "mcr.microsoft.com/azure-cli:latest",
          command: ["/bin/sh", "-c", $calls],
          env: [
            { name: "TMS", value: $tms },
            { name: "TMS_API_CLIENT_ID", value: $tmsApiClientId },
            { name: "INTAKE_CLIENT_ID", value: $intakeClientId },
            { name: "NUMBER", value: $number }
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
job_result() {
  logs "ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$job' and Log_s startswith 'SMOKE-RESULT ' | project Log_s" \
    | jq -r '.[0][0] // empty' | sed 's/^SMOKE-RESULT //'
}
result=$(until_found job_result)
[ -n "$result" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"

for key in transportOrder shipment; do
  jq -e --arg key "$key" '.[$key] and (.[$key].httpStatus | not)' <<<"$result" >/dev/null \
    || fail "the TMS answered $(jq -r --arg key "$key" '.[$key].httpStatus // "nothing"' <<<"$result") for the $key"
done

# 1. The Transport Order is Planned, with its Shipment, and Brouwer Dranken's.
jq -e --arg shipper "$brouwer_dranken_id" '.transportOrder | .status == "Planned" and .shipmentNumber and .shipperId == $shipper' <<<"$result" >/dev/null \
  || fail "Transport Order $number isn't Planned for $brouwer_dranken_id: $(jq -c '.transportOrder' <<<"$result")"
shipment_number=$(jq -r '.transportOrder.shipmentNumber' <<<"$result")
pass "Transport Order $number is Planned, as Shipment $shipment_number"

# 2. The Shipment is Released, carried by Kessler Spedition, planned to Van Dijk Transport and reassigned to Kessler.
jq -e --arg shipper "$brouwer_dranken_id" --arg kessler "$kessler_id" \
  '.shipment | .status == "Released" and .shipperId == $shipper and .assignedTo == "Carrier" and .carrierId == $kessler' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number isn't Released, carried by Kessler Spedition: $(jq -c '.shipment | {status, shipperId, assignedTo, carrierId}' <<<"$result")"
pass "Shipment $shipment_number is Released, carried by Kessler Spedition"
jq -e --arg van_dijk "$van_dijk_id" --arg kessler "$kessler_id" \
  '.shipment.assignments | length == 2 and .[0].carrierId == $van_dijk and .[1].carrierId == $kessler' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number's history of assignments isn't Van Dijk Transport then Kessler Spedition: $(jq -c '.shipment.assignments' <<<"$result")"
pass "Shipment $shipment_number was planned to Van Dijk Transport and reassigned to Kessler Spedition"

# 3. It has left both the Planner's lists.
jq -e '.waitingForPlanning == false' <<<"$result" >/dev/null \
  || fail "Transport Order $number is still waiting for planning, or the TMS answered $(jq -c '.waitingForPlanning' <<<"$result") for that list"
jq -e '.toRelease == false' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number is still in the Shipments to release, or the TMS answered $(jq -c '.toRelease' <<<"$result") for that list"
pass "Transport Order $number and Shipment $shipment_number have left the Planner's lists"

# 4. Each planning step on its own Message trail: the tracking contract's keys (coding guidelines §7), which the TMS
# logs in Properties. The event's publish is a hop of its own, in the outbox worker, continuing the trace of the call
# that raised it (OutboxPublisher) rather than sharing its correlation ID as a tag of its own span, so a trail is
# found by OperationId, as the Integration console does, not by Properties alone (smoke-test-intake.sh check 4).
# Azure Monitor names the TMS after its Container Apps environment too: [<environment>]/tms-api.
trail_summary() {
  logs "let trail = AppTraces | where tostring(Properties.correlationId) == '$1' and isnotempty(OperationId) | distinct OperationId;
union AppRequests, AppTraces, AppDependencies
| where OperationId in (trail) and AppRoleName endswith 'tms-api'
| summarize rows = count(),
    flows = make_set_if(tostring(Properties.flow), isnotempty(tostring(Properties.flow))),
    outcomes = make_set_if(tostring(Properties.outcome), isnotempty(tostring(Properties.outcome))),
    shipmentNumbers = make_set_if(tostring(Properties.shipmentNumber), isnotempty(tostring(Properties.shipmentNumber))),
    parties = make_set_if(tostring(Properties.partyId), isnotempty(tostring(Properties.partyId))),
    transportOrderNumbers = make_set_if(tostring(Properties.transportOrderNumber), isnotempty(tostring(Properties.transportOrderNumber))),
    published = make_set_if(Name, isnotempty(Name) and Name startswith 'publish ')" \
    | jq -c '.[0] | select((.[0] | tonumber) > 0) | {flows: (.[1] | fromjson), outcomes: (.[2] | fromjson),
        shipmentNumbers: (.[3] | fromjson), parties: (.[4] | fromjson), transportOrderNumbers: (.[5] | fromjson),
        published: (.[6] | fromjson)}'
}
# Confirms one Message trail of its own flow, Succeeded for this Transport Order and Shipment, that published the
# one event named.
check_trail() {
  local action=$1 correlation_id=$2 event=$3 summary
  summary=$(until_found trail_summary "$correlation_id")
  [ -n "$summary" ] || fail "the Message trail $correlation_id ($action of Transport Order $number) hasn't reached Application Insights after 10 minutes"
  jq -e --arg shipment "$shipment_number" --arg party "$brouwer_dranken_id" --arg number "$number" --arg event "publish $event" \
    '.flows == ["transport-planning"] and .outcomes == ["Succeeded"] and .shipmentNumbers == [$shipment]
     and .parties == [$party] and .transportOrderNumbers == [$number] and .published == [$event]' \
    <<<"$summary" >/dev/null \
    || fail "the Message trail $correlation_id doesn't show Transport Order $number's $action Succeeded on the transport-planning flow for Shipment $shipment_number, $brouwer_dranken_id, publishing $event: $summary"
  pass "Transport Order $number's $action published $event on its own Message trail $correlation_id"
}
assign_correlation_id=$(jq -r '.shipment.correlationId' <<<"$result")
reassign_correlation_id=$(jq -r '.shipment.assignments[1].correlationId' <<<"$result")
check_trail assignment "$assign_correlation_id" ShipmentPlanned
check_trail reassignment "$reassign_correlation_id" ShipmentReassigned

# The release's correlation ID is its own, and isn't shown by any read: found as the one Succeeded reassign-or-release
# trail under this Transport Order and Shipment that isn't the reassignment's.
find_release_correlation_id() {
  logs "AppTraces
| where tostring(Properties.transportOrderNumber) == '$1' and tostring(Properties.flow) == 'transport-planning'
    and tostring(Properties.outcome) == 'Succeeded' and tostring(Properties.shipmentNumber) == '$2'
    and tostring(Properties.correlationId) !in ('$3', '$4') and AppRoleName endswith 'tms-api'
| distinct tostring(Properties.correlationId)" | jq -r '.[0][0] // empty'
}
release_correlation_id=$(until_found find_release_correlation_id "$number" "$shipment_number" "$assign_correlation_id" "$reassign_correlation_id")
[ -n "$release_correlation_id" ] || fail "no Message trail for Transport Order $number's release, other than its assignment and reassignment, after 10 minutes"
check_trail release "$release_correlation_id" ShipmentReleased

tms_ui="https://$(az containerapp show --name tms-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Open in the TMS UI:"
echo "  Shipment:  $tms_ui/shipments/$shipment_number"
echo "Open in the Integration console, one row per planning step:"
echo "  Search:    $console/?key=$number"
