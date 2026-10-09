#!/usr/bin/env bash
# Post-deploy presenter smoke run of the Over capacity day (P10.8, not in CI): user story 14. The TMS seeds one day in
# its first week Over capacity when it first starts (a rigid truck Off the road, a CE Driver on leave, and four Own fleet
# Transport Orders, 80 pallet places against 77). Signed in to the Azure CLI as the presenter the estate was
# provisioned for, run it once without arguments to see that day:
#
#   infra/smoke-test-over-capacity-day.sh
#
# Then book one more Transport Order from Rotterdam as Brouwer Dranken in the Partner portal, collected that day,
# assign it to Own fleet as the Planner in the TMS UI (the Over capacity warning shows, and the assignment goes ahead),
# reassign it to Kessler Spedition, and run it again with the Transport Order number the portal showed:
#
#   infra/smoke-test-over-capacity-day.sh <Transport Order number>
#
# It checks that:
#   1. the seeded day is Over capacity in the Capacity view, with a Vehicle fewer than the Own fleet has and a Driver
#      fewer at work (without a number, this is all it checks, and it prints the day to book for);
#   2. the Transport Order is Planned for a Shipment carried by Kessler Spedition, collecting on that day, whose history
#      of assignments shows Own fleet first and then Kessler Spedition;
#   3. the day is still Over capacity with the Shipment gone, and the Shipment's collection day is one the Capacity view
#      shows Over capacity;
#   4. the assignment and the reassignment each ran on a Message trail of their own, under the Transport Order number,
#      publishing ShipmentPlanned and ShipmentReassigned on the Shipment events topic in turn.
# The warning itself is shown only to the Planner, in the TMS UI, when the assignment is made; nothing keeps it, so the
# presenter sees it on screen. The TMS's queries answer only a principal holding one of its roles, so a throwaway
# Container Apps job reads them as the intake identity. It prints only what the checks need, which this script reads
# from Log Analytics: never a body, as the Transport Order holds the Consignee's personal data. It ends with the TMS
# UI's and the Integration console's pages to open. The presenter's steps are in
# docs/processes/tms/p10-fleet-capacity-planning.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
number=${1:-}
brouwer_dranken_id=SHP-0001
kessler_id=CAR-0002

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ -z $number || $number =~ ^TO-[0-9]+$ ]] \
  || fail "usage: $0 [<Transport Order number>], such as TO-0000042"

tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
echo "TMS: $tms"
[ -z "$number" ] || echo "Transport Order: $number"

job="smoke-over-capacity-$(date +%s)"
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  rm -f "$job_definition"
  az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
}
trap cleanup EXIT

# 1 to 3. The TMS's answers, as the intake identity. The job is described as a definition file: `az containerapp job
# create --args` takes a leading -c for its own flag.
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
    }

days = get("/v1/capacity/days")
result = {"days": days if not isinstance(days, list) else [{
    "date": day["date"],
    "availableVehicles": day["availableVehicles"],
    "drivers": len(day["shifts"]),
    "over": day["load"]["over"],
    "capacityPalletPlaces": day["load"]["capacityPalletPlaces"],
    "demandPalletPlaces": day["load"]["demandPalletPlaces"],
} for day in days]}

number = os.environ["NUMBER"]
if number:
    order = get("/v1/transport-orders/" + number)
    # Only what the checks need: the Transport Order and the Shipment hold the Consignee'"'"'s personal data.
    result["transportOrder"] = order if "httpStatus" in order else {
        "status": order["status"],
        "shipmentNumber": order["shipmentNumber"],
        "shipperId": order["shipper"]["id"],
        "collectionDate": order["transportOrder"]["collection"]["window"]["earliest"][:10],
    }
    shipment_number = result["transportOrder"].get("shipmentNumber")
    if shipment_number:
        shipment = get("/v1/shipments/" + shipment_number)
        result["shipment"] = shipment if "httpStatus" in shipment else {
            "status": shipment["status"],
            "correlationId": shipment["correlationId"],
            **carried_by(shipment["carriedBy"]),
            "assignments": [{**carried_by(a["carriedBy"]), "correlationId": a["correlationId"]} for a in shipment["assignments"]],
        }
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
          name: "smoke-over-capacity",
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

jq -e '.days | type == "array"' <<<"$result" >/dev/null \
  || fail "the TMS answered $(jq -r '.days.httpStatus // "nothing"' <<<"$result") for the Capacity view"

# 1. The seeded day: Over capacity, with a Vehicle fewer than the six of the Own fleet and a Driver fewer than the
# seven at work on a weekday.
over_days=$(jq -c '[.days[] | select(.over)]' <<<"$result")
jq -e 'length >= 1' <<<"$over_days" >/dev/null \
  || fail "no open day is Over capacity; did the TMS seed the demo's Over capacity day? $(jq -c '.days' <<<"$result")"
over_days=$(jq -c '[.[] | select(.availableVehicles == 5 and .drivers == 6)]' <<<"$over_days")
jq -e 'length >= 1' <<<"$over_days" >/dev/null \
  || fail "no Over capacity day has five Vehicles and six Drivers at work, as the seeded one does: $(jq -c '[.days[] | select(.over)]' <<<"$result")"
seeded_day=$(jq -r '.[0].date' <<<"$over_days")
pass "$seeded_day is Over capacity: $(jq -r '.[0] | "\(.demandPalletPlaces) of \(.capacityPalletPlaces) pallet places"' <<<"$over_days"), with a Vehicle off the road and a Driver on leave"

if [ -z "$number" ]; then
  echo
  echo "Book one more Transport Order from Rotterdam as Brouwer Dranken, collected on $seeded_day, assign it to Own fleet"
  echo "as the Planner, see the Over capacity warning, reassign it to Kessler Spedition, and run this again with its number."
  exit 0
fi

for key in transportOrder shipment; do
  jq -e --arg key "$key" '.[$key] and (.[$key].httpStatus | not)' <<<"$result" >/dev/null \
    || fail "the TMS answered $(jq -r --arg key "$key" '.[$key].httpStatus // "nothing"' <<<"$result") for the $key"
done

# 2. The Transport Order is Planned for Brouwer Dranken, collecting on an Over capacity day, with its Shipment now
# carried by Kessler Spedition after Own fleet.
jq -e --arg shipper "$brouwer_dranken_id" '.transportOrder | .status == "Planned" and .shipmentNumber and .shipperId == $shipper' <<<"$result" >/dev/null \
  || fail "Transport Order $number isn't Planned for $brouwer_dranken_id: $(jq -c '.transportOrder' <<<"$result")"
shipment_number=$(jq -r '.transportOrder.shipmentNumber' <<<"$result")
collection_date=$(jq -r '.transportOrder.collectionDate' <<<"$result")
jq -e --arg date "$collection_date" 'any(.date == $date)' <<<"$over_days" >/dev/null \
  || fail "Transport Order $number collects on $collection_date, which isn't an Over capacity day ($(jq -c '[.[].date]' <<<"$over_days")); book it for $seeded_day"
pass "Transport Order $number is Planned, as Shipment $shipment_number, collecting on the Over capacity day $collection_date"
jq -e --arg kessler "$kessler_id" '.shipment | .assignedTo == "Carrier" and .carrierId == $kessler' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number isn't carried by Kessler Spedition: $(jq -c '.shipment | {status, assignedTo, carrierId}' <<<"$result")"
jq -e --arg kessler "$kessler_id" '.shipment.assignments | length == 2 and .[0].assignedTo == "OwnFleet" and .[1].carrierId == $kessler' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number's history of assignments isn't Own fleet then Kessler Spedition: $(jq -c '.shipment.assignments' <<<"$result")"
pass "Shipment $shipment_number was assigned to Own fleet and reassigned to Kessler Spedition"

# 3. With the Shipment off Own fleet, the day carries what it was seeded with and is still Over capacity: reassigning it
# was the Planner's way out for the one Shipment, not for the seeded day. The Capacity view above was read after the
# reassignment, so $over_days already counts the Shipment as Kessler Spedition's.
pass "$collection_date is still Over capacity with Shipment $shipment_number on Kessler Spedition"

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

tms_ui="https://$(az containerapp show --name tms-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Open in the TMS UI:"
echo "  Capacity:  $tms_ui/capacity/days"
echo "  Shipment:  $tms_ui/shipments/$shipment_number"
echo "Open in the Integration console, one row per planning step:"
echo "  Search:    $console/?key=$number"
