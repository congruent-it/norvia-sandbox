#!/usr/bin/env bash
# Post-deploy presenter smoke run of Proof of delivery (P6.11, not in CI). Book Rotterdam to Düsseldorf as Brouwer Dranken
# and get its Shipment Booked and carried by Van Dijk Transport, as in the presenter smoke run of Carrier booking
# (docs/processes/tms/p4-carrier-booking.md, which leaves Kessler Spedition in Auto-reject; restore it afterwards), then run
# this, signed in to the Azure CLI as the presenter the estate was provisioned for, with its Transport Order number. Do not
# track it first: this delivers it. The Persona's Auto-progress, if it is on, would send a Proof of delivery of its own.
#
#   infra/smoke-test-proof-of-delivery.sh <Transport Order number>
#
# It checks that:
#   1. as Van Dijk Transport, through API Management, PU, DEP and POD each return 202 Accepted and the Shipment is Delivered,
#      listed in the Planner's 'Delivered without Proof of delivery';
#   2. a Proof of delivery with the sample CMR returns 202 Accepted, the Shipment leaves that list, and the Proof of delivery
#      is on its timeline as the current one, with its file;
#   3. the Proof of delivery's blob is gone from the inbound container;
#   4. a Proof of delivery for a reference no Carrier booking has is accepted, then lands on the Proof of delivery queue's
#      dead-letter queue with ShipmentNotFound, and its blob is still in the inbound container;
#   5. a body over 25 MB is refused with 413;
#   6. each call is findable in Application Insights by its correlation ID: in API Management, in the Carrier Proof of
#      delivery Logic App and in the TMS, and for the refused body in API Management.
# A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS's queries answer only a
# principal holding one of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the
# TMS as the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a body,
# as the Transport Order holds the Consignee's personal data, and never a token or a key. The queue's dead-letter queue is
# read with the presenter's own Azure Service Bus Data Receiver right, by locking a message and releasing it again, and the
# inbound container with the presenter's own Storage Blob Data Reader right. The presenter's steps are in
# docs/processes/tms/p6-proof-of-delivery.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
number=${1:-}
van_dijk_id=CAR-0001
queue=carrier-proof-of-delivery
repo=$(cd "$(dirname "$0")/.." && pwd)
sample_cmr=$repo/src/PartnerPortal/Norvia.PartnerPortal.Web/CarrierShipments/sample-cmr.pdf

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $number =~ ^TO-[0-9]+$ ]] \
  || fail "usage: $0 <Transport Order number>, such as TO-0000042"
[ -f "$sample_cmr" ] || fail "the sample CMR isn't at $sample_cmr"

arm="https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers"
partner_api=$AZURE_ESTATE_PARTNER_API_URL
tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
tenant=$(az account show --query tenantId --output tsv)
van_dijk_client_id=$(tr ';' '\n' <<<"$AZURE_ESTATE_PERSONA_CLIENT_IDS" | sed -n 's/^van-dijk-transport=//p')
van_dijk_key=$(az rest --method post --url "$arm/Microsoft.ApiManagement/service/$AZURE_ESTATE_API_MANAGEMENT/subscriptions/van-dijk-transport/listSecrets?api-version=2024-05-01" \
  --query primaryKey --output tsv)
proof_logic_app=$AZURE_ESTATE_PROOF_OF_DELIVERY_LOGIC_APP
inbound_account=$AZURE_ESTATE_INBOUND_STORAGE_ACCOUNT
inbound_container=$AZURE_ESTATE_INBOUND_CONTAINER
cmr_base64=$(base64 --wrap=0 "$sample_cmr")
cmr_bytes=$(stat --format=%s "$sample_cmr")
echo "TMS: $tms"
echo "Transport Order: $number"

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

# Runs the Carrier's calls in a throwaway Container Apps job and prints what it saw, as one JSON document. $1 holds the
# steps: a JSON array of {name, kind, ...}, where kind is track (statusCode, wait: the status to wait for), proof
# (bookingRef, null for the Shipment's own, and the document ID's suffix, which is waited for on the timeline when
# await is true) or oversize. The job is described as a definition file: `az containerapp job create --args` takes a
# leading -c for its own flag.
calls='set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
PERSONA_TOKEN=$(curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d grant_type=client_credentials -d client_id="$VAN_DIJK_CLIENT_ID" -d scope="$PARTNER_API_CLIENT_ID/.default" \
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
    data = None if body is None else (body if isinstance(body, bytes) else json.dumps(body).encode())
    request = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            text = response.read()
            return response.status, response.headers, (json.loads(text) if text else None)
    except urllib.error.HTTPError as error:
        return error.code, error.headers, None
    except OSError:
        return None, {}, None

def tms(path):
    status, _, body = call("GET", os.environ["TMS"] + path, {"Authorization": "Bearer " + os.environ["TMS_TOKEN"]})
    return body if status == 200 else {"httpStatus": status}

def partner(path, body):
    return call("POST", os.environ["PARTNER_API"] + path, {
        "Authorization": "Bearer " + os.environ["PERSONA_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["VAN_DIJK_KEY"],
        "Content-Type": "application/json"}, body)

def is_listed(shipment_number):
    listed = tms("/v1/shipments/delivered-without-proof-of-delivery")
    return isinstance(listed, list) and any(s["shipmentNumber"] == shipment_number for s in listed)

order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
if not order.get("shipmentNumber"):
    print("SMOKE-RESULT " + json.dumps({"shipmentNumber": None}))
    raise SystemExit(0)
shipment_number = order.get("shipmentNumber")
shipment = tms("/v1/shipments/" + shipment_number)
confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
result = {"shipmentNumber": shipment_number, "status": shipment.get("status"), "carrierId": (shipment.get("carriedBy", {}).get("carrier") or {}).get("id"),
          "reference": confirmed[0]["carrierBookingReference"] if confirmed else None, "steps": []}
if result["status"] != "Booked":
    print("SMOKE-RESULT " + json.dumps(result))
    raise SystemExit(0)

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2)
run = os.environ["RUN"]
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    seen = {"name": step["name"]}
    if step["kind"] == "track":
        event = {"bookingRef": result["reference"], "eventId": "SMOKE-%s-%d" % (run, index), "statusCode": step["statusCode"],
                 "eventTime": (base + timedelta(minutes=10 * index)).isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
        status, headers, _ = partner("/carrier-tracking", event)
        seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"))
        for _ in range(24):
            now = tms("/v1/shipments/" + shipment_number)
            if now.get("status") == step["wait"]:
                break
            time.sleep(5)
        seen["status"] = now.get("status")
        if step["wait"] == "Delivered":
            seen["listed"] = is_listed(shipment_number)
    elif step["kind"] == "proof":
        document_id = "SMOKE-%s-%s" % (run, step["name"])
        proof = {"bookingRef": step["bookingRef"] or result["reference"], "documentId": document_id, "receivedBy": "A. Schmitt",
                 "deliveryTime": (base + timedelta(minutes=30)).isoformat(), "place": "Düsseldorf", "postcode": "40591", "country": "DEU",
                 "condition": "CLEAN", "files": [{"name": "sample-cmr.pdf", "contentType": "application/pdf", "content": os.environ["CMR_BASE64"]}]}
        status, headers, _ = partner("/carrier-proofs-of-delivery", proof)
        seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"), documentId=document_id)
        if step["await"]:
            for _ in range(36):
                timeline = tms("/v1/shipments/%s/tracking-events" % shipment_number)
                if isinstance(timeline, dict) and any(p["documentId"] == document_id for p in timeline.get("proofsOfDelivery", [])):
                    break
                time.sleep(5)
            # The list reads what the timeline does, but is asked again for a few seconds before it is taken as still listed.
            for _ in range(6):
                seen["listed"] = is_listed(shipment_number)
                if not seen["listed"]:
                    break
                time.sleep(5)
    else:
        # Just over the 25 MB API Management allows, built in memory (hence the long job timeout); the body is never parsed, so it
        # need not be a valid Proof of delivery.
        status, headers, _ = partner("/carrier-proofs-of-delivery", ("{\"documentId\": \"SMOKE-%s-oversize\", \"padding\": \"%s\"}" % (run, "A" * (25 * 1024 * 1024 + 1))).encode())
        seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"))
    result["steps"].append(seen)

timeline = tms("/v1/shipments/%s/tracking-events" % shipment_number)
result["proofs"] = [{"documentId": p["documentId"], "current": p["isCurrent"], "files": [{"name": f["name"], "contentType": f["contentType"], "sizeBytes": f["sizeBytes"]} for f in p["files"]]}
                    for p in timeline.get("proofsOfDelivery", [])] if isinstance(timeline, dict) else None
print("SMOKE-RESULT " + json.dumps(result))
PYTHON'
run_steps() {
  local job="smoke-proof-of-delivery-$(date +%s)"
  echo "$job" > "$job_file"
  run=$(date +%Y%m%d%H%M%S%N)
  jq -n \
    --arg location "$AZURE_LOCATION" \
    --arg portalIdentity "$AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID" --arg intakeIdentity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
    --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
    --arg calls "$calls" --arg job "$job" --arg tenant "$tenant" --arg partnerApi "$partner_api" --arg partnerApiClientId "$AZURE_ESTATE_PARTNER_API_CLIENT_ID" \
    --arg vanDijk "$van_dijk_client_id" --arg vanDijkKey "$van_dijk_key" \
    --arg portalClientId "$AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
    --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg number "$number" \
    --arg run "$run" --arg cmr "$cmr_base64" --arg steps "$1" '{
      location: $location,
      identity: { type: "UserAssigned", userAssignedIdentities: { ($portalIdentity): {}, ($intakeIdentity): {} } },
      properties: {
        environmentId: $environment,
        configuration: {
          triggerType: "Manual",
          replicaTimeout: 900,
          replicaRetryLimit: 0,
          manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 },
          secrets: [ { name: "van-dijk-key", value: $vanDijkKey } ]
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
              { name: "VAN_DIJK_CLIENT_ID", value: $vanDijk },
              { name: "VAN_DIJK_KEY", secretRef: "van-dijk-key" },
              { name: "INTAKE_CLIENT_ID", value: $intakeClientId },
              { name: "TMS", value: $tms },
              { name: "TMS_API_CLIENT_ID", value: $tmsApiClientId },
              { name: "NUMBER", value: $number },
              { name: "RUN", value: $run },
              { name: "CMR_BASE64", value: $cmr },
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
  [ "$status" = Succeeded ] || fail "the Carrier's calls ended $status; see the logs of job execution $execution"
  local ran=$job
  job_result() {
    logs "ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$ran' and Log_s startswith 'SMOKE-RESULT ' | project Log_s" \
      | jq -r '.[0][0] // empty' | sed 's/^SMOKE-RESULT //'
  }
  until_found job_result
  az containerapp job delete --name "$ran" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  : > "$job_file"
}

# The messages on the queue's dead-letter queue, as the presenter: each is locked and released again, never settled.
# One line each: {correlationId, reason}.
service_bus="https://$AZURE_ESTATE_SERVICE_BUS_NAMESPACE.servicebus.windows.net"
dead_letters() {
  local token headers location
  token=$(az account get-access-token --resource https://servicebus.azure.net --query accessToken --output tsv)
  local locations=()
  for _ in $(seq 1 50); do
    headers=$(curl -sS -D - -o /dev/null -X POST "$service_bus/$queue/\$deadletterqueue/messages/head?timeout=5" \
      -H "Authorization: Bearer $token" | tr -d '\r')
    head -1 <<<"$headers" | grep -q ' 201 ' || break
    location=$(grep -i '^location:' <<<"$headers" | cut -d' ' -f2)
    locations+=("$location")
    jq -nc \
      --arg correlationId "$(grep -i '^correlationid:' <<<"$headers" | cut -d' ' -f2- | tr -d '"')" \
      --arg reason "$(grep -i '^deadletterreason:' <<<"$headers" | cut -d' ' -f2- | tr -d '"')" \
      '{correlationId: $correlationId, reason: $reason}'
  done
  for location in "${locations[@]}"; do
    curl -sS -o /dev/null -X PUT "$location" -H "Authorization: Bearer $token"
  done
}
# Finds the message of a correlation ID, waiting for it, and prints its dead-letter reason.
dead_letter_reason() {
  dead_letters | jq -r --arg id "$1" 'select(.correlationId == $id) | .reason' | head -1
}

# The inbound container's blob of a correlation ID, as the presenter, who can only read: prints exists or gone, and nothing
# when the container can't be read, which is never taken for gone.
blob_state() {
  local exists
  exists=$(az storage blob exists --account-name "$inbound_account" --container-name "$inbound_container" --name "$1" \
    --auth-mode login --query exists --output tsv 2>/dev/null) || return 0
  case $exists in
    true) echo exists ;;
    false) echo gone ;;
  esac
}
# Waits for the blob to be gone, which the Logic App does once the TMS has accepted the Proof of delivery.
blob_gone() {
  [ "$(blob_state "$1")" = gone ] && echo gone || true
}

# Deliver the Booked Shipment, send its Proof of delivery, then one for a reference that no Carrier booking has, then a body
# that is too large.
result=$(run_steps "$(jq -nc '[
  {name: "PU", kind: "track", statusCode: "PU", wait: "Collected"},
  {name: "DEP", kind: "track", statusCode: "DEP", wait: "In transit"},
  {name: "POD", kind: "track", statusCode: "POD", wait: "Delivered"},
  {name: "cmr", kind: "proof", bookingRef: null, await: true},
  {name: "unknown", kind: "proof", bookingRef: "SMOKE-NO-SUCH-REFERENCE", await: false},
  {name: "oversize", kind: "oversize"}]')")
[ -n "$result" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
shipment_number=$(jq -r '.shipmentNumber // empty' <<<"$result")
[ -n "$shipment_number" ] || fail "Transport Order $number has no Shipment, or the TMS doesn't answer for it"
jq -e --arg van_dijk "$van_dijk_id" '.carrierId == $van_dijk and .reference != null' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(jq -c '{status, carrierId, reference}' <<<"$result")"
jq -e '.status == "Booked" and (.steps | length) > 0' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number is $(jq -r '.status' <<<"$result"), not Booked: book a new one, as this run delivers it"

# 1. Delivered, and listed without a Proof of delivery.
for step in PU:Collected DEP:"In transit" POD:Delivered; do
  IFS=: read -r code status <<<"$step"
  jq -e --arg code "$code" --arg status "$status" '.steps[] | select(.name == $code) | .httpStatus == 202 and .status == $status' <<<"$result" >/dev/null \
    || fail "Van Dijk Transport's $code didn't get 202 Accepted and move Shipment $shipment_number to $status: $(jq -c --arg code "$code" '.steps[] | select(.name == $code)' <<<"$result")"
  pass "$code returned 202 Accepted and Shipment $shipment_number is $status"
done
jq -e '.steps[] | select(.name == "POD") | .listed' <<<"$result" >/dev/null \
  || fail "Delivered Shipment $shipment_number isn't in 'Delivered without Proof of delivery'"
pass "Shipment $shipment_number is in 'Delivered without Proof of delivery'"

# 2. The Proof of delivery with the sample CMR: accepted, off the list, and on the timeline with its file.
cmr_id=$(jq -r '.steps[] | select(.name == "cmr") | .correlationId // empty' <<<"$result")
jq -e '.steps[] | select(.name == "cmr") | .httpStatus == 202' <<<"$result" >/dev/null \
  || fail "the Proof of delivery with the sample CMR didn't get 202 Accepted: $(jq -c '.steps[] | select(.name == "cmr")' <<<"$result")"
[ -n "$cmr_id" ] || fail "API Management's 202 for the Proof of delivery carries no correlation ID"
pass "the Proof of delivery with the sample CMR returned 202 Accepted (correlation ID $cmr_id)"
jq -e '.steps[] | select(.name == "cmr") | .listed == false' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number is still in 'Delivered without Proof of delivery' once its Proof of delivery was on the timeline"
pass "Shipment $shipment_number has left 'Delivered without Proof of delivery'"
cmr_document=$(jq -r '.steps[] | select(.name == "cmr") | .documentId' <<<"$result")
jq -e --arg document "$cmr_document" --argjson bytes "$cmr_bytes" '.proofs | any(.documentId == $document and .current
    and (.files | length) == 1 and .files[0].name == "sample-cmr.pdf" and .files[0].contentType == "application/pdf" and .files[0].sizeBytes == $bytes)' \
  <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number's timeline doesn't hold the current Proof of delivery with the sample CMR: $(jq -c '.proofs' <<<"$result")"
pass "Shipment $shipment_number's timeline holds the Proof of delivery as the current one, with sample-cmr.pdf ($cmr_bytes bytes)"

# 3. The blob is gone once the TMS has it.
[ "$(until_found blob_gone "$cmr_id")" = gone ] \
  || fail "the Proof of delivery's blob $cmr_id is still in the $inbound_container container after the wait, or the presenter can't read it"
pass "the Proof of delivery's blob is gone from the $inbound_container container"

# 4. The unknown reference is accepted, then dead-lettered with ShipmentNotFound, and its blob stays for investigation.
unknown_id=$(jq -r '.steps[] | select(.name == "unknown") | .correlationId // empty' <<<"$result")
jq -e '.steps[] | select(.name == "unknown") | .httpStatus == 202' <<<"$result" >/dev/null \
  || fail "the Proof of delivery for an unknown reference didn't get 202 Accepted: $(jq -c '.steps[] | select(.name == "unknown")' <<<"$result")"
[ -n "$unknown_id" ] || fail "API Management's 202 for the unknown reference carries no correlation ID"
pass "the Proof of delivery for an unknown reference returned 202 Accepted (correlation ID $unknown_id)"
reason=$(until_found dead_letter_reason "$unknown_id")
[ "$reason" = ShipmentNotFound ] \
  || fail "the unknown reference's message isn't on the $queue dead-letter queue with ShipmentNotFound after 10 minutes: ${reason:-not there}"
pass "the unknown reference's message is on the $queue dead-letter queue, dead-lettered with ShipmentNotFound"
[ "$(blob_state "$unknown_id")" = exists ] \
  || fail "the unknown reference's blob $unknown_id isn't in the $inbound_container container, or the presenter can't read it"
pass "the unknown reference's blob is still in the $inbound_container container"

# 5. Too large.
oversize_id=$(jq -r '.steps[] | select(.name == "oversize") | .correlationId // empty' <<<"$result")
jq -e '.steps[] | select(.name == "oversize") | .httpStatus == 413' <<<"$result" >/dev/null \
  || fail "a body over 25 MB wasn't refused with 413: $(jq -c '.steps[] | select(.name == "oversize")' <<<"$result")"
pass "a body over 25 MB was refused with 413 (correlation ID ${oversize_id:-none})"

# 6. Each call by its correlation ID. Azure Monitor names the TMS after its Container Apps environment too:
# [<environment>]/tms-api.
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
found "the Proof of delivery, by correlation ID $cmr_id," "$cmr_id" "$AZURE_ESTATE_API_MANAGEMENT" "$proof_logic_app" tms-api
found "the unknown reference's Proof of delivery, by correlation ID $unknown_id," "$unknown_id" "$AZURE_ESTATE_API_MANAGEMENT" "$proof_logic_app" tms-api
[ -n "$oversize_id" ] || fail "API Management's 413 carries no correlation ID"
found "the refused body, by correlation ID $oversize_id," "$oversize_id" "$AZURE_ESTATE_API_MANAGEMENT"

tms_ui="https://$(az containerapp show --name tms-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Open in the TMS UI:"
echo "  Shipment:  $tms_ui/shipments/$shipment_number"
echo "Open in the Integration console, by the Shipment number and by each correlation ID:"
echo "  Shipment:  $console/?key=$shipment_number"
echo "  Proof of delivery: $console/?key=$cmr_id"
echo "  Unknown reference: $console/?key=$unknown_id"
echo "  Too large: $console/?key=$oversize_id"
