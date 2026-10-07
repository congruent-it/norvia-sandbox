#!/usr/bin/env bash
# Post-deploy presenter smoke run of Exception handling (P7.17, not in CI). It runs user story 25 (Van Dijk Transport reports
# NDL on Brouwer Dranken's Rotterdam to Düsseldorf Shipment, the Exception opens, Brouwer Dranken is notified, a new window is
# agreed, the Shipment is delivered and the Exception is resolved Redelivered) and user story 26 (a far ETA on a Booked
# Kessler Spedition Shipment, cancel, re-release to Van Dijk Transport). The Carriers' calls are the script's; the Planner's
# actions are the presenter's, in the TMS UI, as in the presenter smoke run of Carrier booking: the script stops and says what
# to do. Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for, with two Transport
# Order numbers:
#
#   infra/smoke-test-exception-handling.sh <story 25 Transport Order number> <story 26 Transport Order number>
#
# Get both Booked first, as in the presenter smoke run of Carrier booking (docs/processes/tms/p4-carrier-booking.md): the first
# carried by Van Dijk Transport, the second by Kessler Spedition (set it to Auto-confirm for it, and leave the Persona's Auto-progress
# off for both, as the script sends the Tracking events itself). Do not track either first.
# It checks that:
#   1. story 25: PU, DEP and NDL as Van Dijk Transport return 202 Accepted, and one Delivery failed Exception opens for the
#      Shipment, In transit;
#   2. the presenter's Notify Shipper ends Sent in the TMS, and the Shipper notification Logic App ran for it;
#   3. the presenter's agreed delivery window published ShipmentDeliveryWindowAgreed;
#   4. POD delivers the Shipment and its Delivery failed Exception is marked cause cleared, still Open;
#   5. the presenter's resolution as Redelivered leaves the list of Open Exceptions and published ExceptionResolved;
#   6. story 26: a far ETA, sent by Kessler Spedition on a Booked Shipment, opens a Late delivery Exception, the Shipment still Booked;
#   7. the presenter's cancellation leaves Kessler Spedition's Carrier booking Cancelled, the Shipment in Shipments to release
#      as Cancelled by Norvia and not flagged Carrier not told, and the Carrier cancellation Logic App ran for it;
#   8. with the Shipper notification endpoint stopped (the Logic App pointed away from the Partner portal), a notification the
#      presenter requests ends Not delivered after the retries;
#   9. the whole path is findable in Application Insights by correlation ID: each Tracking event in API Management, the Carrier-
#      tracking Logic App and the TMS, and the Shipper notification and the cancellation in their Logic Apps and the TMS.
# A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS's queries answer only a principal
# holding one of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the TMS as the
# intake identity. It prints only what the checks need: never a body, as the Transport Order holds the Consignee's personal
# data, and never a token or a key. Check 8 stops the endpoint by pointing the Logic App's PARTNER_PORTAL_BASE_URL at an address
# that answers nothing, and restores it afterwards, even when the run fails. What only a Persona's page shows (the Shipper's
# notification inbox, Kessler Spedition's "cancelled by Norvia") the script lists at the end for the presenter to open.
# The presenter's steps are in docs/processes/tms/p7-exception-handling.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
story25=${1:-}
story26=${2:-}
brouwer_dranken_id=SHP-0001
van_dijk_id=CAR-0001
kessler_id=CAR-0002

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $story25 =~ ^TO-[0-9]+$ && $story26 =~ ^TO-[0-9]+$ && $story25 != "$story26" ]] \
  || fail "usage: $0 <story 25 Transport Order number> <story 26 Transport Order number>, such as TO-0000042 TO-0000043"
[ -t 0 ] || fail "the Planner's actions are the presenter's, so run this in a terminal"

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
echo "TMS: $tms"
echo "Story 25 Transport Order: $story25"
echo "Story 26 Transport Order: $story26"

tracking_logic_app=$AZURE_ESTATE_TRACKING_LOGIC_APP
notification_logic_app=$AZURE_ESTATE_SHIPPER_NOTIFICATION_LOGIC_APP
cancellation_logic_app=$AZURE_ESTATE_CARRIER_CANCELLATION_LOGIC_APP
# Put back whatever the Shipper notification Logic App was calling, if the run ends while it is pointed away from the Partner portal.
restore_portal=
job_file=$(mktemp)
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  local job
  job=$(cat "$job_file")
  rm -f "$job_definition" "$job_file"
  [ -z "$job" ] || az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  if [ -n "$restore_portal" ]; then
    az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$notification_logic_app" --resource-group "$resource_group" \
      --settings "PARTNER_PORTAL_BASE_URL=$restore_portal" --output none \
      || echo "Put the Shipper notification Logic App's PARTNER_PORTAL_BASE_URL back to $restore_portal" >&2
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

# Runs the Carrier's calls in a throwaway Container Apps job, then reads the Shipment's state, and prints what it saw as one
# JSON document. $1 holds the steps: a JSON array of {name, statusCode, etaDays (or null), wait (the Shipment status to wait for,
# or null)}. The job is described as a definition file: `az containerapp job create --args` takes a leading -c for its own flag.
# $2 is the Transport Order number, $3 the Persona's name, $4 the minutes added to the event times, $5 the Exception type to
# wait for after the last step (or "").
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

def open_exceptions(shipment_number):
    listed = tms("/v1/exceptions/open")
    if not isinstance(listed, list):
        return listed
    return [{"exceptionNumber": e["exceptionNumber"], "type": e["type"], "causes": e["numberOfCauses"], "causeCleared": e["causeCleared"],
             "afterDelivered": e["afterDelivered"], "lastNotification": e.get("lastShipperNotificationStatus")}
            for e in listed if e["shipmentNumber"] == shipment_number]

order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
shipment_number = order.get("shipmentNumber")
if not shipment_number:
    print("SMOKE-RESULT " + json.dumps({"shipmentNumber": None}))
    raise SystemExit(0)
shipment = tms("/v1/shipments/" + shipment_number)
confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
result = {"shipmentNumber": shipment_number, "carrierId": (shipment.get("carriedBy", {}).get("carrier") or {}).get("id"),
          "reference": confirmed[0]["carrierBookingReference"] if confirmed else None, "steps": []}

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2) + timedelta(minutes=int(os.environ["OFFSET_MINUTES"]))
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    event = {"bookingRef": result["reference"], "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["statusCode"],
             "eventTime": (base + timedelta(minutes=10 * index)).isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
    if step["etaDays"] is not None:
        event["eta"] = (datetime.now(timezone.utc).replace(microsecond=0) + timedelta(days=step["etaDays"])).strftime("%Y-%m-%dT%H:%M:%SZ")
    status, headers, _ = call("POST", os.environ["PARTNER_API"] + "/carrier-tracking", {
        "Authorization": "Bearer " + os.environ["PERSONA_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["PERSONA_KEY"],
        "Content-Type": "application/json"}, event)
    seen = {"name": step["name"], "httpStatus": status, "correlationId": headers.get("x-correlation-id")}
    if step["wait"]:
        for _ in range(24):
            if tms("/v1/shipments/" + shipment_number).get("status") == step["wait"]:
                break
            time.sleep(5)
    result["steps"].append(seen)

wanted = os.environ["WAIT_FOR_EXCEPTION"]
for _ in range(24 if wanted else 0):
    found = open_exceptions(shipment_number)
    if isinstance(found, list) and any(e["type"] == wanted for e in found):
        break
    time.sleep(5)

# The Shipment as the Planner sees it now.
shipment = tms("/v1/shipments/" + shipment_number)
to_release = tms("/v1/shipments/to-release")
released = [s for s in to_release if s["shipmentNumber"] == shipment_number] if isinstance(to_release, list) else []
outcome = (released[0].get("lastCarrierBookingOutcome") or {}) if released else {}
result["status"] = shipment.get("status")
result["carrierId"] = (shipment.get("carriedBy", {}).get("carrier") or {}).get("id")
result["carrierBookings"] = [{"number": b["carrierBookingNumber"], "carrierId": b["carrier"]["id"], "status": b["status"]}
                             for b in shipment.get("carrierBookings", [])]
result["toRelease"] = {"listed": bool(released), "status": outcome.get("status"), "reasonText": outcome.get("reasonText"),
                       "carrierNotTold": outcome.get("carrierNotTold")}
result["exceptions"] = open_exceptions(shipment_number)
print("SMOKE-RESULT " + json.dumps(result))
PYTHON'
run_steps() {
  local job="smoke-exception-handling-$(date +%s)" persona=$3 run
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
    --arg run "$run" --arg offset "$4" --arg waitForException "$5" --arg steps "$1" '{
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
              { name: "WAIT_FOR_EXCEPTION", value: $waitForException },
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
# The Shipment's state without sending anything, for what the presenter just did.
look() { run_steps '[]' "$1" "$2" 0 "${3:-}"; }

step_accepted() {
  jq -e --arg name "$2" '.steps[] | select(.name == $name) | .httpStatus == 202' <<<"$1" >/dev/null \
    || fail "$3's $2 didn't get 202 Accepted: $(jq -c --arg name "$2" '.steps[] | select(.name == $name)' <<<"$1")"
}
step_id() { jq -r --arg name "$2" '.steps[] | select(.name == $name) | .correlationId' <<<"$1"; }
exception_of() { jq -c --arg type "$2" '[.exceptions[]? | select(.type == $type)] | .[0] // empty' <<<"$1"; }
wait_for_presenter() {
  echo
  echo "$1"
  read -r -p "Press Enter once it is done... "
}

# Application Insights: the roles whose rows carry a value, and which of a Shipment's exception-handling correlation IDs reached a role.
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
# The correlation IDs of a Shipment's Planner actions that a role also logged: the action's trail runs on into the Logic App.
action_ids_reaching() {
  logs "let ids = AppTraces | where tostring(Properties.shipmentNumber) == '$1' and tostring(Properties.flow) == 'exception-handling'
    and tostring(Properties.outcome) == 'Succeeded' and AppRoleName endswith 'tms-api' | distinct tostring(Properties.correlationId);
union AppRequests, AppTraces, AppDependencies
| where tostring(Properties.correlationId) in (ids) and AppRoleName =~ '$2'
| distinct tostring(Properties.correlationId)" | jq -r '.[][0]'
}
# A Message trail of the Shipment's Planner actions that published an event, by the event's name.
published_on_trail() {
  logs "let ids = AppTraces | where tostring(Properties.shipmentNumber) == '$1' and tostring(Properties.flow) == 'exception-handling'
    and tostring(Properties.outcome) == 'Succeeded' and AppRoleName endswith 'tms-api' | distinct tostring(Properties.correlationId);
let trail = AppTraces | where tostring(Properties.correlationId) in (ids) and isnotempty(OperationId) | distinct OperationId;
union AppRequests, AppTraces, AppDependencies
| where OperationId in (trail) and AppRoleName endswith 'tms-api' and Name == 'publish $2'
| distinct Name" | jq -r '.[][0]'
}
published() {
  local shipment=$1 event=$2
  [ -n "$(until_found published_on_trail "$shipment" "$event")" ] \
    || fail "no Message trail of Shipment $shipment's Planner actions published $event in Application Insights after 10 minutes"
}

# Story 25. 1. PU, DEP and NDL as Van Dijk Transport.
echo
echo "Story 25"
result=$(run_steps "$(jq -nc '[
  {name: "PU", statusCode: "PU", etaDays: null, wait: "Collected"},
  {name: "DEP", statusCode: "DEP", etaDays: null, wait: "In transit"},
  {name: "NDL", statusCode: "NDL", etaDays: null, wait: null}]')" "$story25" van-dijk-transport 0 DeliveryFailed)
shipment25=$(jq -r '.shipmentNumber // empty' <<<"$result")
[ -n "$shipment25" ] || fail "Transport Order $story25 has no Shipment, or the TMS doesn't answer for it"
jq -e --arg van_dijk "$van_dijk_id" '.carrierId == $van_dijk and .reference != null' <<<"$result" >/dev/null \
  || fail "Shipment $shipment25 isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(jq -c '{status, carrierId, reference}' <<<"$result")"
for code in PU DEP NDL; do step_accepted "$result" "$code" "Van Dijk Transport"; done
ndl_id=$(step_id "$result" NDL)
pass "PU, DEP and NDL returned 202 Accepted (NDL's correlation ID $ndl_id)"
jq -e '.status == "In transit"' <<<"$result" >/dev/null || fail "Shipment $shipment25 isn't In transit: $(jq -c '.status' <<<"$result")"
jq -e '[.exceptions[]? | select(.type == "DeliveryFailed")] | length == 1 and (.[0].afterDelivered | not)' <<<"$result" >/dev/null \
  || fail "Shipment $shipment25 doesn't have exactly one Open Delivery failed Exception, opened before Delivered: $(jq -c '.exceptions' <<<"$result")"
exception25=$(jq -r '[.exceptions[] | select(.type == "DeliveryFailed")][0].exceptionNumber' <<<"$result")
pass "NDL opened Exception $exception25 (Delivery failed) for Shipment $shipment25"

# 2 and 3. The presenter notifies the Shipper and agrees a new window.
wait_for_presenter "In the TMS UI ($tms_ui/exceptions), on Exception $exception25: choose Notify Shipper and send the message as it is,
then Agree delivery window with a latest time in the future."
result=$(look "$story25" van-dijk-transport)
for _ in $(seq 1 12); do
  jq -e --arg type DeliveryFailed '[.exceptions[]? | select(.type == $type)][0].lastNotification == "Sent"' <<<"$result" >/dev/null && break
  sleep 10
  result=$(look "$story25" van-dijk-transport)
done
jq -e '[.exceptions[]? | select(.type == "DeliveryFailed")][0].lastNotification == "Sent"' <<<"$result" >/dev/null \
  || fail "Exception $exception25's last Shipper notification isn't Sent: $(jq -c '.exceptions' <<<"$result")"
pass "Exception $exception25's Shipper notification was Sent: Brouwer Dranken's endpoint acknowledged it"
published "$shipment25" ShipmentDeliveryWindowAgreed
pass "the agreed delivery window published ShipmentDeliveryWindowAgreed"

# 4. POD delivers the Shipment, and the cause clears.
result=$(run_steps "$(jq -nc '[{name: "POD", statusCode: "POD", etaDays: null, wait: "Delivered"}]')" "$story25" van-dijk-transport 60 "")
step_accepted "$result" POD "Van Dijk Transport"
jq -e '.status == "Delivered" and ([.exceptions[]? | select(.type == "DeliveryFailed")][0].causeCleared == true)' <<<"$result" >/dev/null \
  || fail "after POD Shipment $shipment25 isn't Delivered with its Delivery failed Exception marked cause cleared: $(jq -c '{status, exceptions}' <<<"$result")"
pass "POD returned 202 Accepted: Shipment $shipment25 is Delivered and Exception $exception25 is marked cause cleared, still Open"
pod_id=$(step_id "$result" POD)

# 5. The presenter resolves it.
wait_for_presenter "In the TMS UI, resolve Exception $exception25 as Redelivered, with a remark."
result=$(look "$story25" van-dijk-transport)
jq -e '[.exceptions[]? | select(.type == "DeliveryFailed")] | length == 0' <<<"$result" >/dev/null \
  || fail "Exception $exception25 is still in the list of Open Exceptions: $(jq -c '.exceptions' <<<"$result")"
resolved=$(until_found logs "AppTraces | where tostring(Properties.shipmentNumber) == '$shipment25' and Message has 'resolved as Redelivered' | distinct Message")
[ -n "$resolved" ] || fail "no 'resolved as Redelivered' for Shipment $shipment25 in Application Insights after 10 minutes"
published "$shipment25" ExceptionResolved
pass "Exception $exception25 was resolved as Redelivered, left the list and published ExceptionResolved"

# Story 26. 6. A far ETA from Kessler Spedition on a Booked Shipment. An NPU carries it without moving the Shipment: every other
# code moves the status forward, and a Cancel is allowed only while the Shipment is Booked.
echo
echo "Story 26"
result=$(run_steps "$(jq -nc '[{name: "ETA", statusCode: "NPU", etaDays: 14, wait: null}]')" "$story26" kessler-spedition 0 LateDelivery)
shipment26=$(jq -r '.shipmentNumber // empty' <<<"$result")
[ -n "$shipment26" ] || fail "Transport Order $story26 has no Shipment, or the TMS doesn't answer for it"
jq -e --arg kessler "$kessler_id" '.carrierId == $kessler and .reference != null' <<<"$result" >/dev/null \
  || fail "Shipment $shipment26 isn't carried by Kessler Spedition with a Confirmed Carrier booking: $(jq -c '{status, carrierId, reference}' <<<"$result")"
step_accepted "$result" ETA "Kessler Spedition"
eta_id=$(step_id "$result" ETA)
jq -e '.status == "Booked" and ([.exceptions[]? | select(.type == "LateDelivery")] | length == 1)' <<<"$result" >/dev/null \
  || fail "Shipment $shipment26 isn't still Booked with an Open Late delivery Exception after the far ETA: $(jq -c '{status, exceptions}' <<<"$result")"
exception26=$(jq -r '[.exceptions[] | select(.type == "LateDelivery")][0].exceptionNumber' <<<"$result")
kessler_booking=$(jq -r --arg kessler "$kessler_id" '[.carrierBookings[] | select(.carrierId == $kessler and .status == "Confirmed")][0].number' <<<"$result")
pass "a far ETA opened Exception $exception26 (Late delivery) for Booked Shipment $shipment26 (correlation ID $eta_id)"

# 7. The presenter cancels, and releases again to Van Dijk Transport.
wait_for_presenter "In the TMS UI ($tms_ui/exceptions), on Exception $exception26: choose Cancel Carrier booking with a reason. Then, in
Shipments to release, reassign Shipment $shipment26 to Van Dijk Transport and release it."
result=$(look "$story26" kessler-spedition)
for _ in $(seq 1 12); do
  jq -e --arg booking "$kessler_booking" '[.carrierBookings[] | select(.number == $booking)][0].status == "Cancelled"' <<<"$result" >/dev/null && break
  sleep 10
  result=$(look "$story26" kessler-spedition)
done
jq -e --arg booking "$kessler_booking" '[.carrierBookings[] | select(.number == $booking)][0].status == "Cancelled"' <<<"$result" >/dev/null \
  || fail "Kessler Spedition's Carrier booking $kessler_booking isn't Cancelled: $(jq -c '.carrierBookings' <<<"$result")"
pass "Kessler Spedition's Carrier booking $kessler_booking is Cancelled and Shipment $shipment26 is $(jq -r '.status' <<<"$result")"
# Kessler's booking leaves the Shipment's last outcome once it is booked again, so the list is read only while it still shows it.
if jq -e '.toRelease.listed' <<<"$result" >/dev/null; then
  jq -e '.toRelease.status == "Cancelled" and (.toRelease.reasonText | . != null and . != "") and (.toRelease.carrierNotTold | not)' <<<"$result" >/dev/null \
    || fail "Shipment $shipment26's Shipments to release row isn't Cancelled by Norvia with its reason, Carrier told: $(jq -c '.toRelease' <<<"$result")"
  pass "Shipment $shipment26 is in Shipments to release as Cancelled by Norvia, with its reason, and the Carrier isn't flagged not told"
else
  echo "NOT CHECKED: Shipment $shipment26 has left Shipments to release already (released again), so its row can't show Cancelled by Norvia."
fi
jq -e --arg van_dijk "$van_dijk_id" '[.carrierBookings[] | select(.carrierId == $van_dijk)] | length >= 1' <<<"$result" >/dev/null \
  || fail "Shipment $shipment26 wasn't released again to Van Dijk Transport: $(jq -c '.carrierBookings' <<<"$result")"
pass "Shipment $shipment26 was released again to Van Dijk Transport"

# 8. With the Shipper notification endpoint stopped, a notification ends Not delivered after the retries.
portal=$(az logicapp config appsettings list --subscription "$AZURE_SUBSCRIPTION_ID" --name "$notification_logic_app" --resource-group "$resource_group" \
  --query "[?name=='PARTNER_PORTAL_BASE_URL'].value | [0]" --output tsv)
[ -n "$portal" ] || fail "the Shipper notification Logic App has no PARTNER_PORTAL_BASE_URL to put back"
echo
echo "Stopping the Shipper notification endpoint (pointing the Logic App away from the Partner portal)..."
restore_portal=$portal
az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$notification_logic_app" --resource-group "$resource_group" \
  --settings "PARTNER_PORTAL_BASE_URL=https://partner-portal.stopped.invalid" --output none
sleep 60
# Story 26's Exception is still open: a notification can be requested from it.
wait_for_presenter "In the TMS UI, on Exception $exception26: choose Notify Shipper and send the message as it is."
result=$(look "$story26" kessler-spedition)
for _ in $(seq 1 18); do
  jq -e '[.exceptions[]? | select(.type == "LateDelivery")][0].lastNotification == "NotDelivered"' <<<"$result" >/dev/null && break
  sleep 10
  result=$(look "$story26" kessler-spedition)
done
jq -e '[.exceptions[]? | select(.type == "LateDelivery")][0].lastNotification == "NotDelivered"' <<<"$result" >/dev/null \
  || fail "with the endpoint stopped, Exception $exception26's last Shipper notification isn't Not delivered after the retries: $(jq -c '.exceptions' <<<"$result")"
pass "with the Shipper notification endpoint stopped, Exception $exception26's notification ended Not delivered after the retries"
az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$notification_logic_app" --resource-group "$resource_group" \
  --settings "PARTNER_PORTAL_BASE_URL=$portal" --output none
restore_portal=

# 9. The whole path, by correlation ID.
echo
found "NDL, by correlation ID $ndl_id," "$ndl_id" "$AZURE_ESTATE_API_MANAGEMENT" "$tracking_logic_app" tms-api
found "the far ETA, by correlation ID $eta_id," "$eta_id" "$AZURE_ESTATE_API_MANAGEMENT" "$tracking_logic_app" tms-api
found "POD, by correlation ID $pod_id," "$pod_id" "$AZURE_ESTATE_API_MANAGEMENT" "$tracking_logic_app" tms-api
notification_ids=$(until_found action_ids_reaching "$shipment25" "$notification_logic_app")
[ -n "$notification_ids" ] || fail "no Planner action on Shipment $shipment25 reached the Shipper notification Logic App in Application Insights after 10 minutes"
found "the Shipper notification, by correlation ID $(head -1 <<<"$notification_ids")," "$(head -1 <<<"$notification_ids")" "$notification_logic_app" tms-api
cancellation_ids=$(until_found action_ids_reaching "$shipment26" "$cancellation_logic_app")
[ -n "$cancellation_ids" ] || fail "no Planner action on Shipment $shipment26 reached the Carrier cancellation Logic App in Application Insights after 10 minutes"
found "the cancellation, by correlation ID $(head -1 <<<"$cancellation_ids")," "$(head -1 <<<"$cancellation_ids")" "$cancellation_logic_app" tms-api

echo
echo "Open in the Partner portal, as the Personas (the script can't sign in as them):"
echo "  Brouwer Dranken's notification inbox: the notification for Exception $exception25's message, newest first"
echo "  Kessler Spedition's booking inbox: the request for Shipment $shipment26 as \"cancelled by Norvia\", with the reason"
echo "Open in the TMS UI:"
echo "  Shipment 25: $tms_ui/shipments/$shipment25"
echo "  Shipment 26: $tms_ui/shipments/$shipment26"
echo "Open in the Integration console, by the Shipment number and by each correlation ID:"
echo "  Shipment 25: $console/?key=$shipment25"
echo "  Shipment 26: $console/?key=$shipment26"
echo "  NDL:         $console/?key=$ndl_id"
echo "  Far ETA:     $console/?key=$eta_id"
echo "  POD:         $console/?key=$pod_id"
