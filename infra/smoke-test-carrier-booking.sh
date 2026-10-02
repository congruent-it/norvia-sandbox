#!/usr/bin/env bash
# Post-deploy presenter smoke run of Carrier booking (P4.9, not in CI). As the Planner in the TMS UI, release Brouwer
# Dranken's Rotterdam to Düsseldorf Shipment to Kessler Spedition while it is set to Auto-reject, release it again to Van
# Dijk Transport once it is back in Shipments to release, and confirm it as Van Dijk Transport in the Partner portal,
# then run this, signed in to the Azure CLI as the presenter the estate was provisioned for, with the Transport Order
# number the portal showed:
#
#   infra/smoke-test-carrier-booking.sh <Transport Order number>
#
# It checks that:
#   1. the Shipment is Booked, carried by Van Dijk Transport, and has left the Planner's lists of Shipments to release
#      and of bookings awaiting an answer;
#   2. it has two Carrier bookings, oldest first: Kessler Spedition's Rejected, with its reason code, and Van Dijk
#      Transport's Confirmed, with the Carrier booking reference it answered with, each reported sent;
#   3. each Carrier booking's release ran on a Message trail, under its Carrier booking number, that published
#      ShipmentReleased and had the P4 booking Logic App's report of it sent recorded;
#   4. each Carrier booking's answer ran on a Message trail of its own, Succeeded for that Carrier, publishing
#      CarrierBookingRejected and CarrierBookingConfirmed in turn on the Shipment events topic.
# The TMS's queries answer only a principal holding one of its roles, so a throwaway Container Apps job reads them as
# the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a
# body, as the Transport Order holds the Consignee's personal data. It ends with the TMS UI's and the Integration
# console's pages to open. The presenter's steps are in docs/processes/tms/p4-carrier-booking.md.
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

job="smoke-carrier-booking-$(date +%s)"
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  rm -f "$job_definition"
  az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
}
trap cleanup EXIT

# 1 and 2. The TMS's answers, as the intake identity. The job is described as a definition file: `az containerapp job
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
    "shipmentNumber": order["shipmentNumber"],
    "shipperId": order["shipper"]["id"],
}}

shipment_number = result["transportOrder"].get("shipmentNumber")
if shipment_number:
    shipment = get("/v1/shipments/" + shipment_number)
    result["shipment"] = shipment if "httpStatus" in shipment else {
        "status": shipment["status"],
        "shipperId": shipment["shipper"]["id"],
        "assignedTo": shipment["carriedBy"]["assignedTo"],
        "carrierId": (shipment["carriedBy"].get("carrier") or {}).get("id"),
        "carrierBookings": [{
            "number": b["carrierBookingNumber"],
            "carrierId": b["carrier"]["id"],
            "status": b["status"],
            "reference": b["carrierBookingReference"],
            "reasonCode": b["reasonCode"],
            "sent": b["sentAt"] is not None,
        } for b in shipment["carrierBookings"]],
    }

to_release = get("/v1/shipments/to-release")
awaiting = get("/v1/shipments/bookings-awaiting-answer")
result["toRelease"] = to_release if not isinstance(to_release, list) else (
    shipment_number is not None and any(s["shipmentNumber"] == shipment_number for s in to_release))
result["awaitingAnswer"] = awaiting if not isinstance(awaiting, list) else (
    shipment_number is not None and any(b["shipmentNumber"] == shipment_number for b in awaiting))
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
          name: "smoke-carrier-booking",
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

# 1. The Shipment is Booked, carried by Van Dijk Transport, and has left both lists.
shipment_number=$(jq -r '.transportOrder.shipmentNumber' <<<"$result")
jq -e --arg shipper "$brouwer_dranken_id" --arg van_dijk "$van_dijk_id" \
  '.shipment | .status == "Booked" and .shipperId == $shipper and .assignedTo == "Carrier" and .carrierId == $van_dijk' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number of Transport Order $number isn't Booked, carried by Van Dijk Transport: $(jq -c '.shipment | {status, shipperId, assignedTo, carrierId}' <<<"$result")"
pass "Shipment $shipment_number of Transport Order $number is Booked, carried by Van Dijk Transport"
jq -e '.toRelease == false' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number is still in the Shipments to release, or the TMS answered $(jq -c '.toRelease' <<<"$result") for that list"
jq -e '.awaitingAnswer == false' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number is still in the bookings awaiting an answer, or the TMS answered $(jq -c '.awaitingAnswer' <<<"$result") for that list"
pass "Shipment $shipment_number has left the Planner's lists"

# 2. Its two Carrier bookings: Kessler Spedition's rejected, then Van Dijk Transport's confirmed, each reported sent.
jq -e --arg kessler "$kessler_id" --arg van_dijk "$van_dijk_id" \
  '.shipment.carrierBookings | length == 2
   and .[0].carrierId == $kessler and .[0].status == "Rejected" and (.[0].reasonCode | . != null)
   and .[1].carrierId == $van_dijk and .[1].status == "Confirmed" and (.[1].reference | . != null and . != "")' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number's Carrier bookings aren't Kessler Spedition's Rejected, then Van Dijk Transport's Confirmed: $(jq -c '.shipment.carrierBookings' <<<"$result")"
rejected_number=$(jq -r '.shipment.carrierBookings[0].number' <<<"$result")
confirmed_number=$(jq -r '.shipment.carrierBookings[1].number' <<<"$result")
pass "Carrier booking $rejected_number was Rejected by Kessler Spedition ($(jq -r '.shipment.carrierBookings[0].reasonCode' <<<"$result")), $confirmed_number Confirmed by Van Dijk Transport"
jq -e '.shipment.carrierBookings | all(.sent)' <<<"$result" >/dev/null \
  || fail "A Carrier booking of Shipment $shipment_number wasn't reported sent: $(jq -c '[.shipment.carrierBookings[] | {number, sent}]' <<<"$result")"
pass "Carrier bookings $rejected_number and $confirmed_number were both reported sent"

# 3 and 4. Each Carrier booking on its Message trails: the tracking contract's keys (coding guidelines §7), which the
# TMS logs in Properties. A publish is a hop of its own in the outbox worker, continuing the trace of the call that
# raised it (OutboxPublisher), so a trail is found by OperationId, as the Integration console does, not by Properties
# alone (smoke-test-transport-planning.sh check 4). Azure Monitor names the TMS after its Container Apps environment
# too: [<environment>]/tms-api.
# The correlation ID of a Carrier booking's trail of one flow: the release's one is found by the report of the booking
# as sent, which continues it, and the answer's is the answer's own, never shown by any read.
find_correlation_id() {
  logs "AppTraces
| where tostring(Properties.carrierBookingNumber) == '$1' and tostring(Properties.flow) == '$2'
    and tostring(Properties.outcome) == 'Succeeded' and AppRoleName endswith 'tms-api'
| distinct tostring(Properties.correlationId)" | jq -r '.[0][0] // empty'
}
trail_summary() {
  logs "let trail = AppTraces | where tostring(Properties.correlationId) == '$1' and isnotempty(OperationId) | distinct OperationId;
union AppRequests, AppTraces, AppDependencies
| where OperationId in (trail) and AppRoleName endswith 'tms-api'
| summarize rows = count(),
    flows = make_set_if(tostring(Properties.flow), isnotempty(tostring(Properties.flow))),
    outcomes = make_set_if(tostring(Properties.outcome), isnotempty(tostring(Properties.outcome))),
    shipmentNumbers = make_set_if(tostring(Properties.shipmentNumber), isnotempty(tostring(Properties.shipmentNumber))),
    parties = make_set_if(tostring(Properties.partyId), isnotempty(tostring(Properties.partyId))),
    carrierBookingNumbers = make_set_if(tostring(Properties.carrierBookingNumber), isnotempty(tostring(Properties.carrierBookingNumber))),
    published = make_set_if(Name, isnotempty(Name) and Name startswith 'publish ')" \
    | jq -c '.[0] | select((.[0] | tonumber) > 0) | {flows: (.[1] | fromjson), outcomes: (.[2] | fromjson),
        shipmentNumbers: (.[3] | fromjson), parties: (.[4] | fromjson), carrierBookingNumbers: (.[5] | fromjson),
        published: (.[6] | fromjson)}'
}
# Finds the trail of one flow of a Carrier booking and returns its summary, with the correlation ID added.
booking_trail() {
  local booking=$1 flow=$2 correlation_id summary
  correlation_id=$(until_found find_correlation_id "$booking" "$flow")
  [ -n "$correlation_id" ] || fail "no Succeeded $flow trail for Carrier booking $booking in Application Insights after 10 minutes"
  summary=$(until_found trail_summary "$correlation_id")
  [ -n "$summary" ] || fail "the Message trail $correlation_id ($flow of Carrier booking $booking) hasn't reached Application Insights after 10 minutes"
  jq -c --arg id "$correlation_id" '. + {correlationId: $id}' <<<"$summary"
}
check_release_trail() {
  local booking=$1 summary
  summary=$(booking_trail "$booking" carrier-booking-report)
  jq -e --arg shipment "$shipment_number" --arg booking "$booking" \
    '(.flows | sort) == ["carrier-booking-report", "transport-planning"] and .outcomes == ["Succeeded"]
     and .shipmentNumbers == [$shipment] and .carrierBookingNumbers == [$booking] and (.published | index("publish ShipmentReleased"))' \
    <<<"$summary" >/dev/null \
    || fail "the release's Message trail doesn't show Carrier booking $booking's release and sent report Succeeded for Shipment $shipment_number, publishing ShipmentReleased: $summary"
  pass "Carrier booking $booking's release published ShipmentReleased and its sent report was recorded, on Message trail $(jq -r '.correlationId' <<<"$summary")"
}
check_answer_trail() {
  local booking=$1 carrier=$2 event=$3 summary
  summary=$(booking_trail "$booking" carrier-booking-answer)
  jq -e --arg shipment "$shipment_number" --arg booking "$booking" --arg carrier "$carrier" --arg event "publish $event" \
    '.flows == ["carrier-booking-answer"] and .outcomes == ["Succeeded"] and .shipmentNumbers == [$shipment]
     and .parties == [$carrier] and .carrierBookingNumbers == [$booking] and .published == [$event]' \
    <<<"$summary" >/dev/null \
    || fail "the answer's Message trail doesn't show Carrier booking $booking's answer Succeeded on the carrier-booking-answer flow for Shipment $shipment_number, $carrier, publishing $event: $summary"
  pass "Carrier booking $booking's answer published $event on its own Message trail $(jq -r '.correlationId' <<<"$summary")"
}
check_release_trail "$rejected_number"
check_answer_trail "$rejected_number" "$kessler_id" CarrierBookingRejected
check_release_trail "$confirmed_number"
check_answer_trail "$confirmed_number" "$van_dijk_id" CarrierBookingConfirmed

tms_ui="https://$(az containerapp show --name tms-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Open in the TMS UI:"
echo "  Shipment:  $tms_ui/shipments/$shipment_number"
echo "Open in the Integration console, by the Shipment number and by each Carrier booking number:"
echo "  Shipment:  $console/?key=$shipment_number"
echo "  Rejected:  $console/?key=$rejected_number"
echo "  Confirmed: $console/?key=$confirmed_number"
