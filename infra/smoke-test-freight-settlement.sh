#!/usr/bin/env bash
# Post-deploy presenter smoke run of Freight settlement, the TMS side (P9.14, not in CI). It runs user story 14 (release Brouwer
# Dranken's Rotterdam to Düsseldorf Shipment to Van Dijk Transport and see it fail with NoMatchingLane, release it with a Spot
# price, see the price in Van Dijk's inbox, confirm, deliver, send a clean Proof of delivery and see the Transport Order become
# Billable), a PriceNotAccepted from Van Dijk's inbox, and a Delivered order without a Proof of delivery that Customer service
# releases with a remark. The Carriers' calls are the script's; the Planner's and Customer service's actions, and Van Dijk
# Transport's answers in the Partner portal, are the presenter's: the script stops and says what to do. Run it in a terminal,
# signed in to the Azure CLI as the presenter the estate was provisioned for, with three Transport Order numbers:
#
#   infra/smoke-test-freight-settlement.sh <priced-by-Spot Transport Order number> <PriceNotAccepted Transport Order number> <no-POD Transport Order number>
#
# Get the first two assigned to Van Dijk Transport and Planned, but not released: Rotterdam to Düsseldorf as Brouwer Dranken,
# whose Lane Van Dijk's Carrier Rate Card deliberately lacks. Get the third Booked and carried by Kessler Spedition, as in the
# presenter smoke run of Carrier booking (docs/processes/tms/p4-carrier-booking.md), with Kessler Spedition's Auto-progress
# off, as the script sends the Tracking events itself. Do not track any of them first. Keep Van Dijk Transport on its manual answer.
# It checks that:
#   1. story 14: the first Shipment is in Shipments to release with the pricing failure NoMatchingLane, and the presenter's
#      Release with Spot price leaves a Requested Carrier booking of Van Dijk Transport whose Carrier cost is one Freight line,
#      with the source Spot and the remark;
#   2. after the presenter's Confirm in Van Dijk's inbox, PU, DEP and POD as Van Dijk Transport return 202 Accepted, the
#      Shipment is Delivered, CarrierCostIncurred was published for it, and the order is Not yet billable for NoProofOfDelivery only;
#   3. a clean Proof of delivery returns 202 Accepted, the order is Billable and TransportOrderBillable was published;
#   4. the second Shipment, released with a Spot price and rejected by the presenter as PriceNotAccepted in Van Dijk's inbox, is
#      Planned again, with its Carrier booking Rejected, reason PriceNotAccepted;
#   5. the third order, Delivered by Kessler Spedition without a Proof of delivery, is in Customer service's list of Transport
#      Orders not yet billable with NoProofOfDelivery only, and after the presenter's release with a remark is Billable, with
#      who released it and the remark, and TransportOrderBillable was published;
#   6. the whole path is findable in Application Insights by correlation ID: each Tracking event and the Proof of delivery in
#      API Management, their Logic Apps and the TMS, the Spot price release in the TMS and the Carrier booking Logic App, and
#      the release without a Proof of delivery in the TMS.
# A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS's queries answer only a principal
# holding one of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the TMS as the
# intake identity. It prints only what the checks need: never a body, as the Transport Order holds the Consignee's personal
# data, and never a token or a key. What only a Persona's page shows (Van Dijk's inbox with the price) the script lists at the
# end for the presenter to open. The presenter's steps are in docs/processes/tms/p9-freight-settlement.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
spot_order=${1:-}
rejected_order=${2:-}
no_pod_order=${3:-}
van_dijk_id=CAR-0001
kessler_id=CAR-0002
repo=$(cd "$(dirname "$0")/.." && pwd)
sample_cmr=$repo/src/PartnerPortal/Norvia.PartnerPortal.Web/CarrierShipments/sample-cmr.pdf

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $spot_order =~ ^TO-[0-9]+$ && $rejected_order =~ ^TO-[0-9]+$ && $no_pod_order =~ ^TO-[0-9]+$ ]] \
  && [ "$(printf '%s\n' "$spot_order" "$rejected_order" "$no_pod_order" | sort -u | wc -l)" = 3 ] \
  || fail "usage: $0 <priced-by-Spot Transport Order number> <PriceNotAccepted Transport Order number> <no-POD Transport Order number>, three different ones such as TO-0000042 TO-0000043 TO-0000044"
[ -t 0 ] || fail "the presenter's actions are the presenter's, so run this in a terminal"
[ -f "$sample_cmr" ] || fail "the sample CMR isn't at $sample_cmr"

arm="https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers"
partner_api=$AZURE_ESTATE_PARTNER_API_URL
tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
tms_ui="https://$(az containerapp show --name tms-web --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
tenant=$(az account show --query tenantId --output tsv)
persona_client_id() { tr ';' '\n' <<<"$AZURE_ESTATE_PERSONA_CLIENT_IDS" | sed -n "s/^$1=//p"; }
persona_key() {
  az rest --method post --url "$arm/Microsoft.ApiManagement/service/$AZURE_ESTATE_API_MANAGEMENT/subscriptions/$1/listSecrets?api-version=2024-05-01" \
    --query primaryKey --output tsv
}
cmr_base64=$(base64 --wrap=0 "$sample_cmr")
echo "TMS: $tms"
echo "Spot price Transport Order: $spot_order"
echo "PriceNotAccepted Transport Order: $rejected_order"
echo "No Proof of delivery Transport Order: $no_pod_order"

tracking_logic_app=$AZURE_ESTATE_TRACKING_LOGIC_APP
proof_logic_app=$AZURE_ESTATE_PROOF_OF_DELIVERY_LOGIC_APP
booking_logic_app=$AZURE_ESTATE_BOOKING_LOGIC_APP
job_file=$(mktemp)
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  local job
  job=$(cat "$job_file")
  rm -f "$job_definition" "$job_file"
  [ -z "$job" ] || az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
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

# Runs the Carrier's calls in a throwaway Container Apps job, then reads the Transport Order's and Shipment's state, and prints
# what it saw as one JSON document. $1 holds the steps: a JSON array of {name, kind, ...} where kind is track (statusCode, wait:
# the Shipment status to wait for, or null) or proof (a clean Proof of delivery with the sample CMR). $2 is the Transport Order
# number, $3 the Persona's name, $4 the minutes added to the event times.
calls='set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
PERSONA_TOKEN=$(curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d grant_type=client_credentials -d client_id="$PERSONA_CLIENT_ID" -d scope="$PARTNER_API_CLIENT_ID/.default" \
  -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
  | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])")
export PERSONA_TOKEN
az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
TMS_TOKEN=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
export TMS_TOKEN
python3 - <<"PYTHON"
import json, os, time, urllib.error, urllib.request
from datetime import datetime, timedelta, timezone

def call(method, url, headers, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            text = response.read()
            return response.status, response.headers, (json.loads(text) if text else None)
    except urllib.error.HTTPError as error:
        return error.code, error.headers, None

def tms(path):
    status, _, body = call("GET", os.environ["TMS"] + path, {"Authorization": "Bearer " + os.environ["TMS_TOKEN"]})
    return body if status == 200 else {"httpStatus": status}

def partner(path, body):
    return call("POST", os.environ["PARTNER_API"] + path, {
        "Authorization": "Bearer " + os.environ["PERSONA_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["PERSONA_KEY"],
        "Content-Type": "application/json"}, body)

number = os.environ["NUMBER"]
order = tms("/v1/transport-orders/" + number)
shipment_number = order.get("shipmentNumber")
if not shipment_number:
    print("SMOKE-RESULT " + json.dumps({"shipmentNumber": None}))
    raise SystemExit(0)
shipment = tms("/v1/shipments/" + shipment_number)
confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
result = {"shipmentNumber": shipment_number, "reference": confirmed[0]["carrierBookingReference"] if confirmed else None, "steps": []}

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2) + timedelta(minutes=int(os.environ["OFFSET_MINUTES"]))
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    if step["kind"] == "track":
        event = {"bookingRef": result["reference"], "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["statusCode"],
                 "eventTime": (base + timedelta(minutes=10 * index)).isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
        status, headers, _ = partner("/carrier-tracking", event)
        seen = {"name": step["name"], "httpStatus": status, "correlationId": headers.get("x-correlation-id")}
        if step["wait"]:
            for _ in range(24):
                if tms("/v1/shipments/" + shipment_number).get("status") == step["wait"]:
                    break
                time.sleep(5)
    else:
        document_id = "SMOKE-%s-%s" % (os.environ["RUN"], step["name"])
        proof = {"bookingRef": result["reference"], "documentId": document_id, "receivedBy": "A. Schmitt",
                 "deliveryTime": (base + timedelta(minutes=30)).isoformat(), "place": "Düsseldorf", "postcode": "40591", "country": "DEU",
                 "condition": "CLEAN", "files": [{"name": "sample-cmr.pdf", "contentType": "application/pdf", "content": os.environ["CMR_BASE64"]}]}
        status, headers, _ = partner("/carrier-proofs-of-delivery", proof)
        seen = {"name": step["name"], "httpStatus": status, "correlationId": headers.get("x-correlation-id")}
        for _ in range(36):
            timeline = tms("/v1/shipments/%s/tracking-events" % shipment_number)
            if isinstance(timeline, dict) and any(p["documentId"] == document_id for p in timeline.get("proofsOfDelivery", [])):
                break
            time.sleep(5)
    result["steps"].append(seen)

# The Transport Order and the Shipment as the Planner and Customer service see them now. After a Proof of delivery the order
# is given a minute to become Billable, as the Logic App records it after the call is accepted.
for _ in range(12 if any(s["kind"] == "proof" for s in json.loads(os.environ["STEPS"])) else 0):
    if tms("/v1/transport-orders/" + number).get("billingState") == "Billable":
        break
    time.sleep(5)
order = tms("/v1/transport-orders/" + number)
shipment = tms("/v1/shipments/" + shipment_number)
to_release = tms("/v1/shipments/to-release")
released = [s for s in to_release if s["shipmentNumber"] == shipment_number] if isinstance(to_release, list) else []
not_billable = tms("/v1/transport-orders/not-yet-billable")
unbilled = [o for o in not_billable if o["transportOrderNumber"] == number] if isinstance(not_billable, list) else []
result["status"] = shipment.get("status")
result["carrierId"] = (shipment.get("carriedBy", {}).get("carrier") or {}).get("id")
result["carrierBookings"] = [{"number": b["carrierBookingNumber"], "carrierId": b["carrier"]["id"], "status": b["status"],
                              "reasonCode": b.get("reasonCode"), "carrierCost": b.get("carrierCost")} for b in shipment.get("carrierBookings", [])]
result["pricingFailure"] = (released[0].get("pricingFailure") or {}).get("code") if released else None
result["toReleaseListed"] = bool(released)
result["billingState"] = order.get("billingState")
result["released"] = order.get("proofOfDeliveryRelease") is not None
result["releasedRemark"] = (order.get("proofOfDeliveryRelease") or {}).get("remark")
result["releasedBy"] = (order.get("proofOfDeliveryRelease") or {}).get("releasedBy")
result["notYetBillable"] = {"listed": bool(unbilled), "blockingReasons": unbilled[0]["blockingReasons"] if unbilled else None}
print("SMOKE-RESULT " + json.dumps(result))
PYTHON'
run_steps() {
  local job="smoke-freight-settlement-$(date +%s)" persona=$3 run
  echo "$job" > "$job_file"
  run=$(date +%Y%m%d%H%M%S%N)
  jq -n \
    --arg location "$AZURE_LOCATION" \
    --arg portalIdentity "$AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID" --arg intakeIdentity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
    --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
    --arg calls "$calls" --arg job "$job" --arg tenant "$tenant" --arg partnerApi "$partner_api" --arg partnerApiClientId "$AZURE_ESTATE_PARTNER_API_CLIENT_ID" \
    --arg personaClientId "$(persona_client_id "$persona")" --arg personaKey "$(persona_key "$persona")" \
    --arg portalClientId "$AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
    --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg number "$2" \
    --arg run "$run" --arg offset "$4" --arg cmrBase64 "$cmr_base64" --arg steps "$1" '{
      location: $location,
      identity: { type: "UserAssigned", userAssignedIdentities: { ($portalIdentity): {}, ($intakeIdentity): {} } },
      properties: {
        environmentId: $environment,
        configuration: {
          triggerType: "Manual",
          replicaTimeout: 600,
          replicaRetryLimit: 0,
          manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 },
          secrets: [ { name: "persona-key", value: $personaKey } ]
        },
        template: {
          containers: [{
            name: $job,
            image: "mcr.microsoft.com/azure-cli:latest",
            command: ["/bin/sh", "-c", $calls],
            env: [
              { name: "TENANT", value: $tenant },
              { name: "PARTNER_API", value: $partnerApi },
              { name: "PARTNER_API_CLIENT_ID", value: $partnerApiClientId },
              { name: "PARTNER_PORTAL_CLIENT_ID", value: $portalClientId },
              { name: "PERSONA_CLIENT_ID", value: $personaClientId },
              { name: "PERSONA_KEY", secretRef: "persona-key" },
              { name: "INTAKE_CLIENT_ID", value: $intakeClientId },
              { name: "TMS", value: $tms },
              { name: "TMS_API_CLIENT_ID", value: $tmsApiClientId },
              { name: "NUMBER", value: $number },
              { name: "RUN", value: $run },
              { name: "OFFSET_MINUTES", value: $offset },
              { name: "CMR_BASE64", value: $cmrBase64 },
              { name: "STEPS", value: $steps }
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
  [ "$status" = Succeeded ] || fail "the job's calls ended $status; see the logs of job execution $execution"
  local ran=$job
  job_result() {
    logs "ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$ran' and Log_s startswith 'SMOKE-RESULT ' | project Log_s" \
      | jq -r '.[0][0] // empty' | sed 's/^SMOKE-RESULT //'
  }
  local found
  found=$(until_found job_result)
  [ -n "$found" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
  az containerapp job delete --name "$ran" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  : > "$job_file"
  echo "$found"
}
# The state without sending anything, for what the presenter just did.
look() { run_steps '[]' "$1" "$2" 0; }

step_accepted() {
  jq -e --arg name "$2" '.steps[] | select(.name == $name) | .httpStatus == 202' <<<"$1" >/dev/null \
    || fail "$3's $2 didn't get 202 Accepted: $(jq -c --arg name "$2" '.steps[] | select(.name == $name)' <<<"$1")"
}
step_id() { jq -r --arg name "$2" '.steps[] | select(.name == $name) | .correlationId' <<<"$1"; }
wait_for_presenter() {
  echo
  echo "$1"
  read -r -p "Press Enter once it is done... "
}
# Asks again, for up to three minutes, until the jq test $3 holds for the state of Transport Order $1 carried by Persona $2.
look_until() {
  local result
  result=$(look "$1" "$2")
  for _ in $(seq 1 18); do
    jq -e "$3" <<<"$result" >/dev/null && break
    sleep 10
    result=$(look "$1" "$2")
  done
  echo "$result"
}

# Application Insights: the roles whose rows carry a value.
roles_with() {
  logs "union AppRequests, AppTraces, AppDependencies
| where tostring(Properties) contains '$1'
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
# The correlation IDs of a Shipment's TMS actions of a Flow that a role also logged: the action's trail runs on into the Logic App.
action_ids_reaching() {
  logs "let ids = AppTraces | where tostring(Properties.shipmentNumber) == '$1' and tostring(Properties.flow) == '$2'
    and tostring(Properties.outcome) == 'Succeeded' and AppRoleName endswith 'tms-api' | distinct tostring(Properties.correlationId);
union AppRequests, AppTraces, AppDependencies
| where tostring(Properties.correlationId) in (ids) and AppRoleName =~ '$3'
| distinct tostring(Properties.correlationId)" | jq -r '.[][0]'
}
# Whether the Message trail of correlation ID $1 published event $2 in the TMS.
published_on_correlation() {
  logs "let trail = AppTraces | where tostring(Properties.correlationId) == '$1' and isnotempty(OperationId) | distinct OperationId;
union AppRequests, AppTraces, AppDependencies
| where OperationId in (trail) and AppRoleName endswith 'tms-api' and Name == 'publish $2'
| distinct Name" | jq -r '.[][0]'
}
# Whether a TMS action of Flow $2 on Shipment $1 has a Message trail that published event $3.
published_on_action() {
  logs "let ids = AppTraces | where tostring(Properties.shipmentNumber) == '$1' and tostring(Properties.flow) == '$2'
    and tostring(Properties.outcome) == 'Succeeded' and AppRoleName endswith 'tms-api' | distinct tostring(Properties.correlationId);
let trail = AppTraces | where tostring(Properties.correlationId) in (ids) and isnotempty(OperationId) | distinct OperationId;
union AppRequests, AppTraces, AppDependencies
| where OperationId in (trail) and AppRoleName endswith 'tms-api' and Name == 'publish $3'
| distinct Name" | jq -r '.[][0]'
}
published_by_call() {
  [ -n "$(until_found published_on_correlation "$2" "$3")" ] \
    || fail "the Message trail of $1 (correlation ID $2) didn't publish $3 in Application Insights after 10 minutes"
}
published_by_action() {
  [ -n "$(until_found published_on_action "$2" "$3" "$4")" ] \
    || fail "no Message trail of $1's $3 action published $4 in Application Insights after 10 minutes"
}

deliver_steps=$(jq -nc '[
  {name: "PU", kind: "track", statusCode: "PU", wait: "Collected"},
  {name: "DEP", kind: "track", statusCode: "DEP", wait: "In transit"},
  {name: "POD", kind: "track", statusCode: "POD", wait: "Delivered"}]')
spot_booking_ok='[.carrierBookings[] | select(.carrierId == "'$van_dijk_id'" and .status == "Requested")] | length == 1'
spot_cost_ok='[.carrierBookings[] | select(.status == "Requested")][0].carrierCost
  | .currency == "EUR" and .source.kind == "Spot" and (.source.remark | . != null and . != "") and .total > 0
    and (.chargeLines | length == 1 and .[0].type == "Freight")'

# Story 14. 1. Releasing to Van Dijk Transport fails on its Carrier Rate Card, and the presenter releases with a Spot price.
echo
echo "Story 14"
result=$(look "$spot_order" van-dijk-transport)
shipment_spot=$(jq -r '.shipmentNumber // empty' <<<"$result")
[ -n "$shipment_spot" ] || fail "Transport Order $spot_order has no Shipment, or the TMS doesn't answer for it"
jq -e --arg van_dijk "$van_dijk_id" '.status == "Planned" and .carrierId == $van_dijk' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_spot isn't Planned and assigned to Van Dijk Transport: $(jq -c '{status, carrierId}' <<<"$result")"
jq -e '.toReleaseListed and .pricingFailure == "NoMatchingLane"' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_spot isn't in Shipments to release with the pricing failure NoMatchingLane: $(jq -c '{toReleaseListed, pricingFailure}' <<<"$result")"
pass "Shipment $shipment_spot is in Shipments to release with the pricing failure NoMatchingLane"
wait_for_presenter "In the TMS UI ($tms_ui), try to release Shipment $shipment_spot and see it fail with NoMatchingLane. Then choose Release with Spot price,
enter an amount (such as 480) and the remark \"agreed with Van Dijk by phone\"."
result=$(look_until "$spot_order" van-dijk-transport "$spot_booking_ok")
jq -e "$spot_booking_ok" <<<"$result" >/dev/null \
  || fail "Shipment $shipment_spot has no Requested Carrier booking of Van Dijk Transport after the release with a Spot price: $(jq -c '.carrierBookings' <<<"$result")"
jq -e "$spot_cost_ok" <<<"$result" >/dev/null \
  || fail "the Carrier booking's Carrier cost isn't one Freight line from a Spot price with its remark: $(jq -c '.carrierBookings' <<<"$result")"
pass "the release with a Spot price left a Requested Carrier booking whose Carrier cost is one Freight line, source Spot, with its remark"

# 2. The presenter confirms in Van Dijk's inbox, and the Shipment is delivered.
wait_for_presenter "In the Partner portal, as Van Dijk Transport, open the booking inbox: see the price and that it is a Spot price,
and Confirm the booking for Shipment $shipment_spot."
result=$(look_until "$spot_order" van-dijk-transport '.reference != null')
jq -e '.reference != null' <<<"$result" >/dev/null || fail "Shipment $shipment_spot's Carrier booking isn't Confirmed after the presenter's Confirm"
pass "Van Dijk Transport confirmed the Spot price: the Carrier booking is Confirmed"
result=$(run_steps "$deliver_steps" "$spot_order" van-dijk-transport 0)
for code in PU DEP POD; do step_accepted "$result" "$code" "Van Dijk Transport"; done
pu_spot_id=$(step_id "$result" PU)
dep_spot_id=$(step_id "$result" DEP)
pod_spot_id=$(step_id "$result" POD)
jq -e '.status == "Delivered"' <<<"$result" >/dev/null || fail "Shipment $shipment_spot isn't Delivered after POD: $(jq -c '.status' <<<"$result")"
pass "PU, DEP and POD returned 202 Accepted and Shipment $shipment_spot is Delivered (POD's correlation ID $pod_spot_id)"
published_by_call "Shipment $shipment_spot's POD" "$pod_spot_id" CarrierCostIncurred
pass "Delivering Shipment $shipment_spot published CarrierCostIncurred"
result=$(look_until "$spot_order" van-dijk-transport '.notYetBillable.listed')
jq -e '.billingState == "NotYetBillable" and .notYetBillable.listed and (.notYetBillable.blockingReasons == ["NoProofOfDelivery"])' <<<"$result" >/dev/null \
  || fail "Transport Order $spot_order isn't Not yet billable for NoProofOfDelivery only: $(jq -c '{billingState, notYetBillable}' <<<"$result")"
pass "Transport Order $spot_order is Delivered and Not yet billable, for NoProofOfDelivery only"

# 3. A clean Proof of delivery makes the order Billable.
result=$(run_steps "$(jq -nc '[{name: "cmr", kind: "proof"}]')" "$spot_order" van-dijk-transport 60)
step_accepted "$result" cmr "Van Dijk Transport"
cmr_spot_id=$(step_id "$result" cmr)
jq -e '.billingState == "Billable" and (.notYetBillable.listed | not)' <<<"$result" >/dev/null \
  || fail "after a clean Proof of delivery Transport Order $spot_order isn't Billable and out of Customer service's list: $(jq -c '{billingState, notYetBillable}' <<<"$result")"
published_by_call "Transport Order $spot_order's Proof of delivery" "$cmr_spot_id" TransportOrderBillable
pass "a clean Proof of delivery returned 202 Accepted: Transport Order $spot_order is Billable and TransportOrderBillable was published (correlation ID $cmr_spot_id)"

# Story 14 variant. 4. PriceNotAccepted sends the Shipment back to Planned.
echo
echo "PriceNotAccepted"
result=$(look "$rejected_order" van-dijk-transport)
shipment_rejected=$(jq -r '.shipmentNumber // empty' <<<"$result")
[ -n "$shipment_rejected" ] || fail "Transport Order $rejected_order has no Shipment, or the TMS doesn't answer for it"
jq -e --arg van_dijk "$van_dijk_id" '.status == "Planned" and .carrierId == $van_dijk and .pricingFailure == "NoMatchingLane"' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_rejected isn't Planned, assigned to Van Dijk Transport, with the pricing failure NoMatchingLane: $(jq -c '{status, carrierId, pricingFailure}' <<<"$result")"
wait_for_presenter "In the TMS UI, on Shipment $shipment_rejected: Release with Spot price, any amount and remark."
result=$(look_until "$rejected_order" van-dijk-transport "$spot_booking_ok")
jq -e "$spot_booking_ok" <<<"$result" >/dev/null \
  || fail "Shipment $shipment_rejected has no Requested Carrier booking of Van Dijk Transport after the release with a Spot price: $(jq -c '.carrierBookings' <<<"$result")"
wait_for_presenter "In the Partner portal, as Van Dijk Transport, reject that booking with the reason PriceNotAccepted."
result=$(look_until "$rejected_order" van-dijk-transport '.status == "Planned" and ([.carrierBookings[] | select(.status == "Rejected")] | length >= 1)')
jq -e '.status == "Planned" and ([.carrierBookings[] | select(.status == "Rejected" and .reasonCode == "PriceNotAccepted")] | length == 1)' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_rejected isn't Planned again with a Carrier booking Rejected as PriceNotAccepted: $(jq -c '{status, carrierBookings}' <<<"$result")"
pass "PriceNotAccepted from Van Dijk's inbox sent Shipment $shipment_rejected back to Planned, its Carrier booking Rejected"

# 5. Delivered without a Proof of delivery, released by Customer service with a remark.
echo
echo "Delivered without a Proof of delivery"
result=$(look "$no_pod_order" kessler-spedition)
jq -e --arg kessler "$kessler_id" '.status == "Booked" and .carrierId == $kessler and .reference != null' <<<"$result" >/dev/null \
  || fail "Transport Order $no_pod_order isn't Booked and carried by Kessler Spedition with a Confirmed Carrier booking: $(jq -c '{status, carrierId, reference}' <<<"$result")"
result=$(run_steps "$deliver_steps" "$no_pod_order" kessler-spedition 0)
shipment_no_pod=$(jq -r '.shipmentNumber // empty' <<<"$result")
[ -n "$shipment_no_pod" ] || fail "Transport Order $no_pod_order has no Shipment, or the TMS doesn't answer for it"
for code in PU DEP POD; do step_accepted "$result" "$code" "Kessler Spedition"; done
pod_no_pod_id=$(step_id "$result" POD)
result=$(look_until "$no_pod_order" kessler-spedition '.notYetBillable.listed')
jq -e '.status == "Delivered" and .billingState == "NotYetBillable" and .notYetBillable.listed and (.notYetBillable.blockingReasons == ["NoProofOfDelivery"])' <<<"$result" >/dev/null \
  || fail "Transport Order $no_pod_order isn't Delivered and listed as not yet billable for NoProofOfDelivery only: $(jq -c '{status, billingState, notYetBillable}' <<<"$result")"
pass "Delivered Transport Order $no_pod_order shows in Customer service's list with NoProofOfDelivery"
wait_for_presenter "In the TMS UI ($tms_ui), in Customer service's list of Transport Orders not yet billable, release $no_pod_order without a Proof of delivery,
with the remark \"CMR received by post, scanned in the dossier\"."
result=$(look_until "$no_pod_order" kessler-spedition '.billingState == "Billable"')
jq -e '.billingState == "Billable" and .released and (.releasedBy | . != null and . != "") and (.releasedRemark | . != null and . != "") and (.notYetBillable.listed | not)' <<<"$result" >/dev/null \
  || fail "Transport Order $no_pod_order isn't Billable, released with its remark, and out of the list: $(jq -c '{billingState, released, notYetBillable}' <<<"$result")"
published_by_action "Shipment $shipment_no_pod" "$shipment_no_pod" billing TransportOrderBillable
pass "Transport Order $no_pod_order was released with a remark, is Billable and published TransportOrderBillable"
published_by_call "Shipment $shipment_no_pod's POD" "$pod_no_pod_id" CarrierCostIncurred
pass "Delivering Shipment $shipment_no_pod published CarrierCostIncurred"

# 6. The whole path, by correlation ID.
echo
found "story 14's PU, by correlation ID $pu_spot_id," "$pu_spot_id" "$AZURE_ESTATE_API_MANAGEMENT" "$tracking_logic_app" tms-api
found "story 14's DEP, by correlation ID $dep_spot_id," "$dep_spot_id" "$AZURE_ESTATE_API_MANAGEMENT" "$tracking_logic_app" tms-api
found "story 14's POD, by correlation ID $pod_spot_id," "$pod_spot_id" "$AZURE_ESTATE_API_MANAGEMENT" "$tracking_logic_app" tms-api
found "story 14's Proof of delivery, by correlation ID $cmr_spot_id," "$cmr_spot_id" "$AZURE_ESTATE_API_MANAGEMENT" "$proof_logic_app" tms-api
found "the third order's POD, by correlation ID $pod_no_pod_id," "$pod_no_pod_id" "$AZURE_ESTATE_API_MANAGEMENT" "$tracking_logic_app" tms-api
release_ids=$(until_found action_ids_reaching "$shipment_spot" transport-planning "$booking_logic_app")
[ -n "$release_ids" ] || fail "no Planner action on Shipment $shipment_spot reached the Carrier booking Logic App in Application Insights after 10 minutes"
found "the release with a Spot price, by correlation ID $(head -1 <<<"$release_ids")," "$(head -1 <<<"$release_ids")" "$booking_logic_app" tms-api
billing_ids=$(until_found action_ids_reaching "$shipment_no_pod" billing tms-api)
[ -n "$billing_ids" ] || fail "no release without a Proof of delivery on Shipment $shipment_no_pod in the TMS in Application Insights after 10 minutes"
found "the release without a Proof of delivery, by correlation ID $(head -1 <<<"$billing_ids")," "$(head -1 <<<"$billing_ids")" tms-api

echo
echo "Open in the Partner portal, as Van Dijk Transport (the script can't sign in as the Persona):"
echo "  The booking inbox: the answered request for Shipment $shipment_spot with its Spot price, and the one for Shipment $shipment_rejected as rejected"
echo "Open in the TMS UI:"
echo "  Spot Shipment:                $tms_ui/shipments/$shipment_spot (the Carrier cost, and the billing state, Billable)"
echo "  PriceNotAccepted Shipment:    $tms_ui/shipments/$shipment_rejected"
echo "  Released without a Proof of delivery: $tms_ui/shipments/$shipment_no_pod (the remark and who released it)"
echo "Open in the Integration console, by the Shipment number and by each correlation ID:"
echo "  Spot Shipment: $console/?key=$shipment_spot"
echo "  POD:          $console/?key=$pod_spot_id"
echo "  Proof:        $console/?key=$cmr_spot_id"
echo "  No-POD order: $console/?key=$shipment_no_pod"
