#!/usr/bin/env bash
# Post-deploy presenter smoke run of Shipment tracking (P5.11, not in CI). Book Rotterdam to Düsseldorf as Brouwer Dranken
# and get its Shipment Booked and carried by Van Dijk Transport, as in the presenter smoke run of Carrier booking
# (docs/processes/tms/p4-carrier-booking.md, which leaves Kessler Spedition in Auto-reject; restore it afterwards), then run
# this, signed in to the Azure CLI as the presenter the estate was provisioned for, with its Transport Order number:
#
#   infra/smoke-test-shipment-tracking.sh <Transport Order number>
#
# It checks that:
#   1. as Van Dijk Transport, through API Management, PU, DEP and POD each return 202 Accepted, and the Shipment moves
#      through Collected, In transit and Delivered, listed in the Planner's Shipments in progress until it is Delivered;
#   2. the Shipment's timeline holds the three Tracking events, each of which moved its status;
#   3. a Tracking event for a reference no Carrier booking has is accepted, then lands on the tracking queue's dead-letter
#      queue with ShipmentNotFound;
#   4. each accepted call is findable in Application Insights by its correlation ID, in API Management, the Carrier-tracking
#      Logic App and the TMS;
#   5. with the TMS unreachable for the Carrier-tracking Logic App, a POD is dead-lettered by Service Bus after the queue's
#      maximum delivery count, MaxDeliveryCountExceeded, and, once the TMS is back and the presenter has resubmitted it
#      from the Integration console (the dead-letter step the headline incident relies on), the TMS records it.
# A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS's queries answer only a
# principal holding one of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the
# TMS as the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a body,
# as the Transport Order holds the Consignee's personal data, and never a token or a key. The tracking queue's dead-letter
# queue is read with the presenter's own Azure Service Bus Data Receiver right, by locking a message and releasing it again.
# Check 5 stops the TMS by pointing the Logic App's TMS_BASE_URL at an address that answers nothing, and restores it
# afterwards, even when the run fails. The presenter's steps are in docs/processes/tms/p5-shipment-tracking.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
number=${1:-}
brouwer_dranken_id=SHP-0001
van_dijk_id=CAR-0001
queue=carrier-tracking

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $number =~ ^TO-[0-9]+$ ]] \
  || fail "usage: $0 <Transport Order number>, such as TO-0000042"

arm="https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers"
partner_api=$AZURE_ESTATE_PARTNER_API_URL
tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
tenant=$(az account show --query tenantId --output tsv)
van_dijk_client_id=$(tr ';' '\n' <<<"$AZURE_ESTATE_PERSONA_CLIENT_IDS" | sed -n 's/^van-dijk-transport=//p')
van_dijk_key=$(az rest --method post --url "$arm/Microsoft.ApiManagement/service/$AZURE_ESTATE_API_MANAGEMENT/subscriptions/van-dijk-transport/listSecrets?api-version=2024-05-01" \
  --query primaryKey --output tsv)
echo "TMS: $tms"
echo "Transport Order: $number"

tracking_logic_app=$AZURE_ESTATE_TRACKING_LOGIC_APP
# Put back whatever the Logic App was calling, if the run ends while it is pointed away from the TMS.
restore_tms=
job=
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  rm -f "$job_definition"
  [ -z "$job" ] || az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  if [ -n "$restore_tms" ]; then
    az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$tracking_logic_app" --resource-group "$resource_group" \
      --settings "TMS_BASE_URL=$tms" --output none || echo "Put the Carrier-tracking Logic App's TMS_BASE_URL back to $tms" >&2
  fi
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
# steps: a JSON array of {name, statusCode, bookingRef (null for the Shipment's own), wait (the status to wait for, or
# null)}. The job is described as a definition file: `az containerapp job create --args` takes a leading -c for its own flag.
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

order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
shipment_number = order.get("shipmentNumber")
shipment = tms("/v1/shipments/" + shipment_number) if shipment_number else {}
confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
result = {"shipmentNumber": shipment_number, "status": shipment.get("status"), "carrierId": (shipment.get("carriedBy", {}).get("carrier") or {}).get("id"),
          "reference": confirmed[0]["carrierBookingReference"] if confirmed else None, "steps": []}

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2)
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    reference = step["bookingRef"] or result["reference"]
    event = {"bookingRef": reference, "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["statusCode"],
             "eventTime": (base + timedelta(minutes=10 * index)).isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
    status, headers, _ = call("POST", os.environ["PARTNER_API"] + "/carrier-tracking", {
        "Authorization": "Bearer " + os.environ["PERSONA_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["VAN_DIJK_KEY"],
        "Content-Type": "application/json"}, event)
    seen = {"name": step["name"], "httpStatus": status, "correlationId": headers.get("x-correlation-id")}
    if step["wait"]:
        for _ in range(24):
            now = tms("/v1/shipments/" + shipment_number)
            if now.get("status") == step["wait"]:
                break
            time.sleep(5)
        in_progress = tms("/v1/shipments/in-progress")
        seen["status"] = now.get("status")
        seen["inProgress"] = isinstance(in_progress, list) and any(s["shipmentNumber"] == shipment_number for s in in_progress)
    result["steps"].append(seen)

timeline = tms("/v1/shipments/%s/tracking-events" % shipment_number) if shipment_number else {}
result["timeline"] = timeline if isinstance(timeline, dict) else [{"type": e["type"], "moved": e["movedShipment"]} for e in timeline]
print("SMOKE-RESULT " + json.dumps(result))
PYTHON'
run_steps() {
  job="smoke-shipment-tracking-$(date +%s)"
  jq -n \
    --arg location "$AZURE_LOCATION" \
    --arg portalIdentity "$AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID" --arg intakeIdentity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
    --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
    --arg calls "$calls" --arg job "$job" --arg tenant "$tenant" --arg partnerApi "$partner_api" --arg partnerApiClientId "$AZURE_ESTATE_PARTNER_API_CLIENT_ID" \
    --arg vanDijk "$van_dijk_client_id" --arg vanDijkKey "$van_dijk_key" \
    --arg portalClientId "$AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
    --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg number "$number" \
    --arg run "$run" --arg steps "$1" '{
      location: $location,
      identity: { type: "UserAssigned", userAssignedIdentities: { ($portalIdentity): {}, ($intakeIdentity): {} } },
      properties: {
        environmentId: $environment,
        configuration: {
          triggerType: "Manual",
          replicaTimeout: 600,
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
  job=
}

# The messages on the tracking queue's dead-letter queue, as the presenter: each is locked and released again, never settled.
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

run=$(date +%Y%m%d%H%M%S)

# 1 to 3. Van Dijk Transport's tracking, and a Tracking event for a reference that no Carrier booking has.
result=$(run_steps "$(jq -nc '[
  {name: "PU", statusCode: "PU", bookingRef: null, wait: "Collected"},
  {name: "DEP", statusCode: "DEP", bookingRef: null, wait: "In transit"},
  {name: "POD", statusCode: "POD", bookingRef: null, wait: "Delivered"},
  {name: "unknown", statusCode: "PU", bookingRef: "SMOKE-NO-SUCH-REFERENCE", wait: null}]')")
[ -n "$result" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
shipment_number=$(jq -r '.shipmentNumber // empty' <<<"$result")
[ -n "$shipment_number" ] || fail "Transport Order $number has no Shipment, or the TMS doesn't answer for it"
jq -e --arg van_dijk "$van_dijk_id" '.carrierId == $van_dijk and .reference != null' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(jq -c '{status, carrierId, reference}' <<<"$result")"

# 1. Accepted, and the Shipment follows. It is listed while it is on its way, and leaves the list when Delivered.
for step in PU:Collected:true DEP:"In transit":true POD:Delivered:false; do
  IFS=: read -r code status listed <<<"$step"
  jq -e --arg code "$code" '.steps[] | select(.name == $code) | .httpStatus == 202' <<<"$result" >/dev/null \
    || fail "Van Dijk Transport's $code didn't get 202 Accepted: $(jq -c --arg code "$code" '.steps[] | select(.name == $code)' <<<"$result")"
  jq -e --arg code "$code" --arg status "$status" --argjson listed "$listed" \
    '.steps[] | select(.name == $code) | .status == $status and .inProgress == $listed' <<<"$result" >/dev/null \
    || fail "after $code Shipment $shipment_number isn't $status, listed in Shipments in progress: $listed: $(jq -c --arg code "$code" '.steps[] | select(.name == $code)' <<<"$result")"
  pass "$code returned 202 Accepted and Shipment $shipment_number is $status$([ "$listed" = true ] && echo ', in the Shipments in progress' || echo ', out of them')"
done

# 2. The timeline.
jq -e '.timeline | map(.type) == ["Collected", "Departed", "Delivered"] and all(.moved)' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number's timeline isn't Collected, Departed and Delivered, each moving its status: $(jq -c '.timeline' <<<"$result")"
pass "Shipment $shipment_number's timeline holds Collected, Departed and Delivered, each of which moved its status"

# 3. The unknown reference is accepted, then dead-lettered with ShipmentNotFound.
unknown_id=$(jq -r '.steps[] | select(.name == "unknown") | .correlationId // empty' <<<"$result")
jq -e '.steps[] | select(.name == "unknown") | .httpStatus == 202' <<<"$result" >/dev/null \
  || fail "the Tracking event for an unknown reference didn't get 202 Accepted: $(jq -c '.steps[] | select(.name == "unknown")' <<<"$result")"
[ -n "$unknown_id" ] || fail "API Management's 202 for the unknown reference carries no correlation ID"
pass "the Tracking event for an unknown reference returned 202 Accepted (correlation ID $unknown_id)"
reason=$(until_found dead_letter_reason "$unknown_id")
[ "$reason" = ShipmentNotFound ] \
  || fail "the unknown reference's message isn't on the $queue dead-letter queue with ShipmentNotFound after 10 minutes: ${reason:-not there}"
pass "the unknown reference's message is on the $queue dead-letter queue, dead-lettered with ShipmentNotFound"

# 4. Each accepted call by its correlation ID: API Management, the Logic App and, where the TMS was called, the TMS. Azure
# Monitor names the TMS after its Container Apps environment too: [<environment>]/tms-api.
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
for code in PU DEP POD; do
  correlation_id=$(jq -r --arg code "$code" '.steps[] | select(.name == $code) | .correlationId' <<<"$result")
  found "$code, by correlation ID $correlation_id," "$correlation_id" "$AZURE_ESTATE_API_MANAGEMENT" "$tracking_logic_app" tms-api
done
found "the unknown reference's Tracking event, by correlation ID $unknown_id," "$unknown_id" "$AZURE_ESTATE_API_MANAGEMENT" "$tracking_logic_app" tms-api

# 5. With the TMS unreachable, a POD is retried from the queue and dead-lettered by Service Bus after the maximum delivery
# count. The Shipment is Delivered, so the TMS only logs the POD again once it is resubmitted, with an event ID of its own.
echo "Pointing the Carrier-tracking Logic App away from the TMS..."
restore_tms=yes
az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$tracking_logic_app" --resource-group "$resource_group" \
  --settings "TMS_BASE_URL=https://tms-api.stopped.invalid" --output none
sleep 60
outage=$(run_steps "$(jq -nc '[{name: "POD", statusCode: "POD", bookingRef: null, wait: null}]')")
[ -n "$outage" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
jq -e '.steps[0].httpStatus == 202' <<<"$outage" >/dev/null || fail "the POD sent while the TMS was stopped didn't get 202 Accepted: $(jq -c '.steps[0]' <<<"$outage")"
outage_id=$(jq -r '.steps[0].correlationId' <<<"$outage")
pass "a POD sent while the TMS is stopped returns 202 Accepted (correlation ID $outage_id)"
reason=$(until_found dead_letter_reason "$outage_id")
[ "$reason" = MaxDeliveryCountExceeded ] \
  || fail "the POD sent while the TMS was stopped isn't on the $queue dead-letter queue with MaxDeliveryCountExceeded after 10 minutes: ${reason:-not there}"
pass "the POD is on the $queue dead-letter queue, dead-lettered by Service Bus with MaxDeliveryCountExceeded"

az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$tracking_logic_app" --resource-group "$resource_group" \
  --settings "TMS_BASE_URL=$tms" --output none
restore_tms=
console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "The TMS is back. Resubmit the POD in the Integration console, from the dead-letter queue of $queue:"
echo "  $console/?key=$outage_id"
if [ -t 0 ]; then
  read -r -p "Press Enter once it is resubmitted (the TMS needs the Carrier-tracking Logic App to have restarted first)... "
  recorded() {
    local timeline
    timeline=$(run_steps '[]' | jq -c '.timeline | map(select(.type == "Delivered")) | length') || return 0
    [ "$timeline" -ge 2 ] && echo "$timeline"
  }
  count=$(until_found recorded)
  [ -n "$count" ] || fail "the resubmitted POD isn't in Shipment $shipment_number's timeline after 10 minutes"
  pass "the resubmitted POD was recorded: Shipment $shipment_number's timeline holds $count Delivered events"
else
  echo "Not a terminal, so the resubmission isn't checked. Check Shipment $shipment_number's timeline for a second Delivered event."
fi

tms_ui="https://$(az containerapp show --name tms-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Open in the TMS UI:"
echo "  Shipment:  $tms_ui/shipments/$shipment_number"
echo "Open in the Integration console, by the Shipment number and by each correlation ID:"
echo "  Shipment:  $console/?key=$shipment_number"
for code in PU DEP POD; do
  echo "  $code: $console/?key=$(jq -r --arg code "$code" '.steps[] | select(.name == $code) | .correlationId' <<<"$result")"
done
echo "  Unknown reference: $console/?key=$unknown_id"
