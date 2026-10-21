#!/usr/bin/env bash
# Post-deploy presenter smoke run of the Integration console, slice 2 (not in CI): stop the TMS, find the dead letter,
# resubmit it. Get a Shipment Booked and carried by Van Dijk Transport, as in the presenter smoke run of Carrier booking
# (docs/processes/tms/p4-carrier-booking.md, which leaves Kessler Spedition in Auto-reject; restore it afterwards), then
# run this in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for, with its Transport
# Order number. Do not track it first, and leave the Persona's Auto-progress off: this delivers it.
#
#   infra/smoke-test-integration-console-dead-letters.sh <Transport Order number>
#
# It checks that:
#   1. as Van Dijk Transport, through API Management, PU and DEP return 202 Accepted and the Shipment is In transit;
#   2. with the TMS container app stopped, a POD returns 202 Accepted, and after the queue's maximum delivery count it is on
#      the tracking queue's dead-letter queue, MaxDeliveryCountExceeded, within seconds of its last attempt;
#   3. once the TMS is started and the presenter has resubmitted the message in the Integration console, the Shipment is
#      Delivered, and its Message trail holds the failed attempts of the Carrier-tracking Logic App and the audit event of
#      the resubmit, with the presenter's name;
#   4. a Tracking event for a reference no Carrier booking has is dead-lettered with ShipmentNotFound, and, once the
#      presenter has discarded it with a reason in the console, it is gone from the dead-letter queue and its Message trail
#      holds the audit event of the discard, with the reason.
# It reads as the presenter, and the Carrier's calls are made by a throwaway Container Apps job running as the Partner
# portal, which reads the TMS as the intake identity (ADR 0005). The TMS is stopped with `az containerapp stop`, and started
# again afterwards even when the run fails. It prints only what the checks need: never a body, as the Transport Order holds
# the Consignee's personal data, and never a token or a key. The tracking queue's dead-letter queue is read with the
# presenter's own Azure Service Bus Data Receiver right, by locking a message and releasing it again. The presenter's steps
# are in docs/operations/integration-console.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
number=${1:-}
van_dijk_id=CAR-0001
queue=carrier-tracking

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $number =~ ^TO-[0-9]+$ ]] \
  || fail "usage: $0 <Transport Order number>, such as TO-0000042"
[ -t 0 ] || fail "the resubmit and the discard are the presenter's, in the Integration console, so run this in a terminal"

arm="https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers"
partner_api=$AZURE_ESTATE_PARTNER_API_URL
tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
tms_ui="https://$(az containerapp show --name tms-web --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
tenant=$(az account show --query tenantId --output tsv)
van_dijk_client_id=$(tr ';' '\n' <<<"$AZURE_ESTATE_PERSONA_CLIENT_IDS" | sed -n 's/^van-dijk-transport=//p')
van_dijk_key=$(az rest --method post --url "$arm/Microsoft.ApiManagement/service/$AZURE_ESTATE_API_MANAGEMENT/subscriptions/van-dijk-transport/listSecrets?api-version=2024-05-01" \
  --query primaryKey --output tsv)
tracking_logic_app=$AZURE_ESTATE_TRACKING_LOGIC_APP
echo "TMS: $tms"
echo "Transport Order: $number"

# Start the TMS again, if the run ends while it is stopped.
tms_stopped=
job_file=$(mktemp)
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  local job
  job=$(cat "$job_file")
  rm -f "$job_definition" "$job_file"
  [ -z "$job" ] || az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  if [ -n "$tms_stopped" ]; then
    az containerapp start --name tms-api --resource-group "$resource_group" --output none || echo "Start the TMS again: az containerapp start --name tms-api --resource-group $resource_group" >&2
  fi
}
trap cleanup EXIT

workspace=$(az monitor log-analytics workspace show --resource-group "$resource_group" \
  --workspace-name "$AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE" --query customerId --output tsv)
logs() {
  az rest --method post --resource https://api.loganalytics.io --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
    --body "$(jq -n --arg query "$1" '{query: $query, timespan: "P1D"}')" --query 'tables[0].rows' --output json
}
# Ingestion takes a few minutes, so a check that finds nothing asks again, every $INTERVAL seconds for up to $ATTEMPTS times
# (10 minutes by default).
until_found() {
  local result
  for _ in $(seq 1 "${ATTEMPTS:-30}"); do
    result=$("$@")
    [ -z "$result" ] || { echo "$result"; return; }
    sleep "${INTERVAL:-20}"
  done
}

# Runs the Carrier's tracking calls in a throwaway Container Apps job and prints what it saw, as one JSON document. $1 holds
# the steps: a JSON array of {name, statusCode, bookingRef (null for the Shipment's own), wait (the status to wait for, or
# null)}. $2 is the Shipment's Carrier booking reference when the TMS is stopped, so the job doesn't read it, and empty when
# it is up; then the job also reads the Shipment's status and, for $3 > 0, waits for that status. The job is described as a
# definition file: `az containerapp job create --args` takes a leading -c for its own flag.
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

reference = os.environ["BOOKING_REFERENCE"]
result = {"steps": []}
if reference:
    shipment_number = os.environ["SHIPMENT_NUMBER"]
else:
    order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
    shipment_number = order.get("shipmentNumber")
    shipment = tms("/v1/shipments/" + shipment_number) if shipment_number else {}
    confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
    reference = confirmed[0]["carrierBookingReference"] if confirmed else None
    result.update(shipmentNumber=shipment_number, status=shipment.get("status"),
                  carrierId=(shipment.get("carriedBy", {}).get("carrier") or {}).get("id"), reference=reference)
    if not shipment_number or shipment.get("status") != "Booked":
        print("SMOKE-RESULT " + json.dumps(result))
        raise SystemExit(0)

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2) + timedelta(minutes=int(os.environ["OFFSET_MINUTES"]))
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    event = {"bookingRef": step["bookingRef"] or reference, "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["statusCode"],
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
        seen["status"] = now.get("status")
    result["steps"].append(seen)

if os.environ["READ_STATUS"] == "1":
    result["status"] = tms("/v1/shipments/" + shipment_number).get("status")
print("SMOKE-RESULT " + json.dumps(result))
PYTHON'
run_steps() {
  local job="smoke-console-dead-letters-$(date +%s)"
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
    --arg run "$run" --arg offset "$3" --arg bookingReference "${booking_reference:-}" --arg shipmentNumber "${shipment_number:-}" \
    --arg readStatus "$2" --arg steps "$1" '{
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
              { name: "OFFSET_MINUTES", value: $offset },
              { name: "BOOKING_REFERENCE", value: $bookingReference },
              { name: "SHIPMENT_NUMBER", value: $shipmentNumber },
              { name: "READ_STATUS", value: $readStatus },
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
# Prints gone once the message of a correlation ID is not on the dead-letter queue.
dead_letter_gone() {
  [ -n "$(dead_letter_reason "$1")" ] || echo gone
}

# The audit events the console wrote for a correlation ID, as it reads them for the trail: one line each, {action, actor,
# reason}.
audits() {
  logs "AppTraces
| where Properties has '$1'
| where isnotempty(tostring(Properties.Actor)) and isnotempty(tostring(Properties.Action))
| where tostring(Properties.correlationId) =~ '$1'
| project action = tostring(Properties.Action), actor = tostring(Properties.Actor), reason = tostring(Properties.Reason)" \
    | jq -c '.[] | {action: .[0], actor: .[1], reason: .[2]}'
}
audit_of() {
  audits "$1" | jq -c --arg action "$2" 'select(.action == $action)' | head -1
}
# The Carrier-tracking Logic App's runs under a correlation ID: the first attempt and each delivery again.
attempts() {
  logs "union AppRequests, AppTraces, AppDependencies
| where Properties has '$1'
| extend tracked = parse_json(tostring(Properties.trackedProperties))
| where coalesce(tostring(Properties.correlationId), tostring(tracked.correlationId)) =~ '$1' and AppRoleName =~ '$tracking_logic_app'
| extend runId = tostring(parse_json(tostring(Properties.resource)).runId)
| where isnotempty(runId)
| summarize runs = dcount(runId)" | jq -r '.[0][0] // 0'
}
many_attempts() {
  local runs
  runs=$(attempts "$1")
  [ "$runs" -lt 2 ] || echo "$runs"
}

# 1. Van Dijk Transport collects and departs, so the POD that fails is the one that delivers.
result=$(run_steps "$(jq -nc '[
  {name: "PU", statusCode: "PU", bookingRef: null, wait: "Collected"},
  {name: "DEP", statusCode: "DEP", bookingRef: null, wait: "In transit"}]')" 0 0)
[ -n "$result" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
shipment_number=$(jq -r '.shipmentNumber // empty' <<<"$result")
[ -n "$shipment_number" ] || fail "Transport Order $number has no Shipment, or the TMS doesn't answer for it"
jq -e --arg van_dijk "$van_dijk_id" '.carrierId == $van_dijk and .reference != null' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(jq -c '{status, carrierId, reference}' <<<"$result")"
jq -e '(.steps | length) > 0' <<<"$result" >/dev/null \
  || fail "Shipment $shipment_number is $(jq -r '.status' <<<"$result"), not Booked: book a new one, as this run delivers it"
booking_reference=$(jq -r '.reference' <<<"$result")
for step in PU:Collected DEP:"In transit"; do
  IFS=: read -r code status <<<"$step"
  jq -e --arg code "$code" --arg status "$status" '.steps[] | select(.name == $code) | .httpStatus == 202 and .status == $status' <<<"$result" >/dev/null \
    || fail "Van Dijk Transport's $code didn't get 202 Accepted and move Shipment $shipment_number to $status: $(jq -c --arg code "$code" '.steps[] | select(.name == $code)' <<<"$result")"
  pass "$code returned 202 Accepted and Shipment $shipment_number is $status"
done

# 2. With the TMS stopped, a POD is delivered again until Service Bus dead-letters it.
echo "Stopping the TMS..."
tms_stopped=yes
az containerapp stop --name tms-api --resource-group "$resource_group" --output none
sleep 30
outage=$(run_steps "$(jq -nc '[{name: "POD", statusCode: "POD", bookingRef: null, wait: null}]')" 0 60)
[ -n "$outage" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
jq -e '.steps[0].httpStatus == 202' <<<"$outage" >/dev/null || fail "the POD sent while the TMS was stopped didn't get 202 Accepted: $(jq -c '.steps[0]' <<<"$outage")"
outage_id=$(jq -r '.steps[0].correlationId' <<<"$outage")
pass "a POD sent while the TMS is stopped returns 202 Accepted (correlation ID $outage_id)"
# The queue's lock lasts a minute and it delivers 10 times, so it takes about 10 minutes; once there, the panel shows it on its
# next refresh and this asks every 5 seconds.
reason=$(ATTEMPTS=360 INTERVAL=5 until_found dead_letter_reason "$outage_id")
[ "$reason" = MaxDeliveryCountExceeded ] \
  || fail "the POD sent while the TMS was stopped isn't on the $queue dead-letter queue with MaxDeliveryCountExceeded after 30 minutes: ${reason:-not there}"
pass "the POD is on the $queue dead-letter queue, dead-lettered by Service Bus with MaxDeliveryCountExceeded"

# 3. The TMS is started and the presenter resubmits the message in the console.
echo "Starting the TMS..."
az containerapp start --name tms-api --resource-group "$resource_group" --output none
tms_stopped=
for _ in $(seq 1 30); do
  [ "$(curl -sS -o /dev/null -w '%{http_code}' "$tms/health" || true)" = 200 ] && break
  sleep 10
done
echo
echo "The TMS is back. Resubmit the POD in the Integration console, on the dead-letter queue of $queue:"
echo "  $console/dead-letter-queues/$queue"
echo "  trail: $console/trails/$outage_id"
read -r -p "Press Enter once it is resubmitted... "
delivered=$(run_steps '[]' 1 0 | jq -r '.status')
for _ in $(seq 1 12); do
  [ "$delivered" = Delivered ] && break
  sleep 10
  delivered=$(run_steps '[]' 1 0 | jq -r '.status')
done
[ "$delivered" = Delivered ] || fail "Shipment $shipment_number is $delivered, not Delivered, after the resubmit"
pass "after the resubmit Shipment $shipment_number is Delivered"
[ -z "$(dead_letter_reason "$outage_id")" ] || fail "the resubmitted POD is still on the $queue dead-letter queue"
runs=$(until_found many_attempts "$outage_id")
[ -n "$runs" ] || fail "the trail $outage_id shows fewer than two Carrier-tracking Logic App runs after 10 minutes, so no failed attempts"
pass "the trail shows $runs Carrier-tracking Logic App runs under correlation ID $outage_id: the failed attempts and the one that delivered"
resubmit=$(until_found audit_of "$outage_id" resubmit)
[ -n "$resubmit" ] || fail "the trail $outage_id has no audit event of the resubmit after 10 minutes"
pass "the trail holds the audit event of the resubmit, by $(jq -r '.actor' <<<"$resubmit")"

# 4. A Tracking event for an unknown reference is dead-lettered with ShipmentNotFound, and the presenter discards it.
unknown=$(run_steps "$(jq -nc '[{name: "unknown", statusCode: "PU", bookingRef: "SMOKE-NO-SUCH-REFERENCE", wait: null}]')" 0 0)
[ -n "$unknown" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
jq -e '.steps[0].httpStatus == 202' <<<"$unknown" >/dev/null || fail "the Tracking event for an unknown reference didn't get 202 Accepted: $(jq -c '.steps[0]' <<<"$unknown")"
unknown_id=$(jq -r '.steps[0].correlationId' <<<"$unknown")
pass "the Tracking event for an unknown reference returned 202 Accepted (correlation ID $unknown_id)"
reason=$(ATTEMPTS=60 INTERVAL=5 until_found dead_letter_reason "$unknown_id")
[ "$reason" = ShipmentNotFound ] \
  || fail "the unknown reference's message isn't on the $queue dead-letter queue with ShipmentNotFound after 5 minutes: ${reason:-not there}"
pass "the unknown reference's message is on the $queue dead-letter queue, dead-lettered with ShipmentNotFound"
echo
echo "Discard the message in the Integration console, with a reason:"
echo "  $console/dead-letter-queues/$queue"
echo "  trail: $console/trails/$unknown_id"
read -r -p "Press Enter once it is discarded... "
[ "$(until_found dead_letter_gone "$unknown_id")" = gone ] || fail "the unknown reference's message is still on the $queue dead-letter queue"
pass "the unknown reference's message is gone from the $queue dead-letter queue"
discard=$(until_found audit_of "$unknown_id" discard)
[ -n "$discard" ] || fail "the trail $unknown_id has no audit event of the discard after 10 minutes"
jq -e '.reason != ""' <<<"$discard" >/dev/null || fail "the audit event of the discard holds no reason"
pass "the trail holds the audit event of the discard, by $(jq -r '.actor' <<<"$discard"), with its reason"

echo
echo "Open in the TMS UI:"
echo "  Shipment:  $tms_ui/shipments/$shipment_number"
echo "Open in the Integration console:"
echo "  Resubmitted POD:  $console/trails/$outage_id"
echo "  Discarded:        $console/trails/$unknown_id"
