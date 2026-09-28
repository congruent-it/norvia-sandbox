#!/usr/bin/env bash
# Post-deploy demo smoke run of the Norvia Partner API (P1.10, not in CI). Run it after `azd up`, signed in to the Azure
# CLI as the presenter the estate was provisioned for. It books through API Management and checks that:
#   1. Brouwer Dranken, with its token and subscription key, gets a Transport Order number, and the Transport Order is
#      readable in the TMS, for Brouwer Dranken even though the booking and its headers name another Shipper;
#   2. Van Dijk Transport, a Carrier without the submit role, gets 403, and no intake Logic App run starts;
#   3. a call without a subscription key, with a wrong one, without a token or with a token for another API is refused;
#   4. the correlation ID API Management created is the one on the Logic App run, the Transport Order and the
#      TransportOrderAccepted event, whatever correlation ID the caller sent;
#   5. the accepted and the refused bookings are in Application Insights by correlation ID, and the accepted one also by
#      its Transport Order number.
# A Persona's token can be had only as the Partner portal's identity (ADR 0005), so a throwaway Container Apps job runs
# the Personas' calls as it, and reads the Transport Order back as the intake identity. It prints what it saw, which
# this script reads from Log Analytics; it never prints a token or a key.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
arm="https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers"
partner_api=$AZURE_ESTATE_PARTNER_API_URL
tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
tenant=$(az account show --query tenantId --output tsv)
persona_client_id() { tr ';' '\n' <<<"$AZURE_ESTATE_PERSONA_CLIENT_IDS" | sed -n "s/^$1=//p"; }
subscription_key() {
  az rest --method post --url "$arm/Microsoft.ApiManagement/service/$AZURE_ESTATE_API_MANAGEMENT/subscriptions/$1/listSecrets?api-version=2024-05-01" \
    --query primaryKey --output tsv
}
brouwer_key=$(subscription_key brouwer-dranken)
van_dijk_key=$(subscription_key van-dijk-transport)

# The booking names another active Shipper in its body and headers, and sends a correlation ID of its own. API
# Management must ignore all three.
booking_reference="SMOKE-$(date +%Y%m%d%H%M%S)"
booking=$(jq -c --arg reference "$booking_reference" '.bookingRef = $reference | .shipperId = "SHP-0003"' \
  "$(dirname "$0")/../contracts/partner-api/samples/bookings/minimal.booking.json")

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

echo "Norvia Partner API: $partner_api"

# 3. Refused for the key or the token. These need no Persona token, so they run from here.
refused_for_credentials=()
call() {
  curl -sS -o /dev/null -D - -X POST "$partner_api/transport-orders" -H 'Content-Type: application/json' "$@" -d "$booking" \
    | tr -d '\r'
}
check_refused() {
  local what=$1 headers status correlation_id
  shift
  headers=$(call "$@")
  status=$(head -1 <<<"$headers" | cut -d' ' -f2)
  correlation_id=$(grep -i '^x-correlation-id:' <<<"$headers" | cut -d' ' -f2 || true)
  [ "$status" = 401 ] || fail "$what: API Management answered $status, not 401"
  [ -n "$correlation_id" ] || fail "$what: the refusal carries no correlation ID"
  refused_for_credentials+=("$correlation_id")
  pass "$what is refused (401, correlation ID $correlation_id)"
}
check_refused "a call without a subscription key" -H "Authorization: Bearer not-a-token"
check_refused "a call with a wrong subscription key" -H "Ocp-Apim-Subscription-Key: 00000000000000000000000000000000"
check_refused "a call without a token" -H "Ocp-Apim-Subscription-Key: $brouwer_key"
check_refused "a call with a token for another API" -H "Ocp-Apim-Subscription-Key: $brouwer_key" \
  -H "Authorization: Bearer $(az account get-access-token --query accessToken --output tsv)"

# The test's own subscription, created before the booking, sees the Shipper's events. Its filter keeps to the tracking
# contract (coding guidelines §7). Service Bus deletes it after 10 idle minutes if the cleanup below doesn't run.
run="smoke-partner-api-$(date +%s)"
job=$run
subscription=$run
# The job's definition holds the Personas' subscription keys.
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  rm -f "$job_definition"
  az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  az servicebus topic subscription delete --resource-group "$resource_group" --namespace-name "$AZURE_ESTATE_SERVICE_BUS_NAMESPACE" \
    --topic-name transport-order-events --name "$subscription" --output none 2>/dev/null || true
}
trap cleanup EXIT
az servicebus topic subscription create --resource-group "$resource_group" --namespace-name "$AZURE_ESTATE_SERVICE_BUS_NAMESPACE" \
  --topic-name transport-order-events --name "$subscription" --auto-delete-on-idle PT10M --output none
az servicebus topic subscription rule create --resource-group "$resource_group" --namespace-name "$AZURE_ESTATE_SERVICE_BUS_NAMESPACE" \
  --topic-name transport-order-events --subscription-name "$subscription" --name brouwer-dranken \
  --filter-sql-expression "(partyId = 'SHP-0001' AND resubmitTo IS NULL) OR resubmitTo = '$subscription'" --output none
az servicebus topic subscription rule delete --resource-group "$resource_group" --namespace-name "$AZURE_ESTATE_SERVICE_BUS_NAMESPACE" \
  --topic-name transport-order-events --subscription-name "$subscription" --name '$Default' --output none

# 1 and 2. The Personas' calls, as the Partner portal's identity. The job is described as a definition file:
# `az containerapp job create --args` takes a leading -c for its own flag.
calls='set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
persona_token() {
  curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
    -d grant_type=client_credentials -d client_id="$1" -d scope="$PARTNER_API_CLIENT_ID/.default" \
    -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
    | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])"
}
submit() {
  curl -sS -o "/tmp/$1.json" -D "/tmp/$1.headers" -w "%{http_code}" -X POST "$PARTNER_API/transport-orders" \
    -H "Authorization: Bearer $2" -H "Ocp-Apim-Subscription-Key: $3" -H "Content-Type: application/json" \
    -H "X-Shipper-ID: SHP-0003" -H "X-Correlation-ID: sent-by-the-caller" -d "$BOOKING" > "/tmp/$1.status"
}
submit accepted "$(persona_token "$BROUWER_CLIENT_ID")" "$BROUWER_KEY"
submit refused "$(persona_token "$VAN_DIJK_CLIENT_ID")" "$VAN_DIJK_KEY"
number=$(python3 -c "import json; print(json.load(open(\"/tmp/accepted.json\")).get(\"norviaOrderNo\") or \"\")" 2>/dev/null || true)
: > /tmp/read.json
if [ -n "$number" ]; then
  az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
  token=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
  curl -fsS "$TMS/v1/transport-orders/$number" -H "Authorization: Bearer $token" > /tmp/read.json || true
fi
python3 - <<"PYTHON"
import json

def call(name):
    headers = {}
    for line in open(f"/tmp/{name}.headers"):
        key, _, value = line.partition(":")
        headers[key.strip().lower()] = value.strip()
    try:
        body = json.load(open(f"/tmp/{name}.json"))
    except ValueError:
        body = None
    return {"status": int(open(f"/tmp/{name}.status").read()), "correlationId": headers.get("x-correlation-id"), "body": body}

try:
    order = json.load(open("/tmp/read.json"))
    read = {"shipperId": order["shipper"]["id"], "correlationId": order["correlationId"], "number": order["transportOrderNumber"]}
except (ValueError, KeyError):
    read = None
# Only what the checks need: the Transport Order holds the Consignee'"'"'s personal data.
print("SMOKE-RESULT " + json.dumps({"accepted": call("accepted"), "refused": call("refused"), "read": read}))
PYTHON'
jq -n \
  --arg location "$AZURE_LOCATION" \
  --arg portalIdentity "$AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID" --arg intakeIdentity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
  --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
  --arg calls "$calls" --arg tenant "$tenant" --arg partnerApi "$partner_api" --arg partnerApiClientId "$AZURE_ESTATE_PARTNER_API_CLIENT_ID" \
  --arg brouwer "$(persona_client_id brouwer-dranken)" --arg vanDijk "$(persona_client_id van-dijk-transport)" \
  --arg brouwerKey "$brouwer_key" --arg vanDijkKey "$van_dijk_key" \
  --arg portalClientId "$AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
  --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg booking "$booking" '{
    location: $location,
    identity: { type: "UserAssigned", userAssignedIdentities: { ($portalIdentity): {}, ($intakeIdentity): {} } },
    properties: {
      environmentId: $environment,
      configuration: {
        triggerType: "Manual",
        replicaTimeout: 600,
        replicaRetryLimit: 0,
        manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 },
        secrets: [ { name: "brouwer-key", value: $brouwerKey }, { name: "van-dijk-key", value: $vanDijkKey } ]
      },
      template: {
        containers: [{
          name: "smoke-partner-api",
          image: "mcr.microsoft.com/azure-cli:latest",
          command: ["/bin/sh", "-c", $calls],
          env: [
            { name: "TENANT", value: $tenant },
            { name: "PARTNER_API", value: $partnerApi },
            { name: "PARTNER_API_CLIENT_ID", value: $partnerApiClientId },
            { name: "PARTNER_PORTAL_CLIENT_ID", value: $portalClientId },
            { name: "BROUWER_CLIENT_ID", value: $brouwer },
            { name: "VAN_DIJK_CLIENT_ID", value: $vanDijk },
            { name: "BROUWER_KEY", secretRef: "brouwer-key" },
            { name: "VAN_DIJK_KEY", secretRef: "van-dijk-key" },
            { name: "INTAKE_CLIENT_ID", value: $intakeClientId },
            { name: "TMS", value: $tms },
            { name: "TMS_API_CLIENT_ID", value: $tmsApiClientId },
            { name: "BOOKING", value: $booking }
          ],
          resources: { cpu: 0.5, memory: "1Gi" }
        }]
      }
    }
  }' > "$job_definition"
az containerapp job create --name "$job" --resource-group "$resource_group" --yaml "$job_definition" --output none
rm -f "$job_definition"
execution=$(az containerapp job start --name "$job" --resource-group "$resource_group" --query name --output tsv)
while
  status=$(az containerapp job execution show --name "$job" --resource-group "$resource_group" \
    --job-execution-name "$execution" --query properties.status --output tsv)
  [ "$status" = Running ] || [ "$status" = Processing ]
do
  sleep 10
done
[ "$status" = Succeeded ] || fail "the Personas' calls ended $status; see the logs of job execution $execution"

workspace=$(az monitor log-analytics workspace show --resource-group "$resource_group" \
  --workspace-name "$AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE" --query customerId --output tsv)
logs() {
  az rest --method post --resource https://api.loganalytics.io --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
    --body "$(jq -n --arg query "$1" '{query: $query}')" --query 'tables[0].rows' --output json
}
# Ingestion takes a few minutes.
for _ in $(seq 1 30); do
  result=$(logs "ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$job' and Log_s startswith 'SMOKE-RESULT ' | project Log_s" \
    | jq -r '.[0][0] // empty' | sed 's/^SMOKE-RESULT //')
  [ -z "$result" ] || break
  sleep 20
done
[ -n "$result" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"

# 1. Accepted, and readable in the TMS for the Shipper from the token.
accepted_id=$(jq -r '.accepted.correlationId // empty' <<<"$result")
number=$(jq -r '.accepted.body.norviaOrderNo // empty' <<<"$result")
jq -e '.accepted.status == 201 and .accepted.body.result == "ACCEPTED"' <<<"$result" >/dev/null \
  || fail "Brouwer Dranken's booking wasn't accepted: $(jq -c '.accepted' <<<"$result")"
pass "Brouwer Dranken's booking is accepted as Transport Order $number (correlation ID $accepted_id)"
jq -e '.read.number == .accepted.body.norviaOrderNo' <<<"$result" >/dev/null || fail "Transport Order $number isn't readable in the TMS"
jq -e '.read.shipperId == "SHP-0001"' <<<"$result" >/dev/null \
  || fail "Transport Order $number is for $(jq -r '.read.shipperId' <<<"$result"), not Brouwer Dranken (SHP-0001) from the token"
pass "Transport Order $number is readable in the TMS, for SHP-0001 from the token, not SHP-0003 from the booking and its headers"

# 4. One correlation ID, created by API Management.
[ -n "$accepted_id" ] && [ "$accepted_id" != sent-by-the-caller ] || fail "API Management passed on the caller's correlation ID"
jq -e --arg id "$accepted_id" '.accepted.body.messageId == $id and .read.correlationId == $id' <<<"$result" >/dev/null \
  || fail "the answer or the Transport Order carries another correlation ID than $accepted_id"
pass "the answer and the Transport Order carry the correlation ID API Management created"

runs() {
  az rest --method get --url "$arm/Microsoft.Web/sites/$AZURE_ESTATE_INTAKE_LOGIC_APP/hostruntime/runtime/webhooks/workflow/api/management/workflows/transport-order-intake/runs?api-version=2018-11-01&\$filter=ClientTrackingId%20eq%20'$1'" \
    --query 'length(value)' --output tsv
}
[ "$(runs "$accepted_id")" = 1 ] || fail "no intake Logic App run has the client tracking ID $accepted_id"
pass "the intake Logic App run has the correlation ID as its client tracking ID"

service_bus_token=$(az account get-access-token --resource https://servicebus.azure.net --query accessToken --output tsv)
messages="https://$AZURE_ESTATE_SERVICE_BUS_NAMESPACE.servicebus.windows.net/transport-order-events/subscriptions/$subscription/messages/head?timeout=60"
event_found=
for _ in 1 2 3 4 5; do
  headers=$(curl -sS -D - -o /dev/null -X DELETE "$messages" -H "Authorization: Bearer $service_bus_token")
  broker_properties=$(grep -i '^brokerproperties:' <<<"$headers" | cut -d' ' -f2- || true)
  [ -n "$broker_properties" ] || break
  if jq -e --arg id "$accepted_id" '.Label == "TransportOrderAccepted" and .CorrelationId == $id' <<<"$broker_properties" >/dev/null; then
    event_found=yes
    break
  fi
done
[ -n "$event_found" ] || fail "no TransportOrderAccepted event with correlation ID $accepted_id reached transport-order-events"
pass "the TransportOrderAccepted event carries the correlation ID"

# 2. Refused at the edge.
refused_id=$(jq -r '.refused.correlationId // empty' <<<"$result")
jq -e '.refused.status == 403' <<<"$result" >/dev/null \
  || fail "Van Dijk Transport's booking got $(jq -r '.refused.status' <<<"$result"), not 403"
[ -n "$refused_id" ] || fail "Van Dijk Transport's refusal carries no correlation ID"
pass "Van Dijk Transport's booking is refused with 403 (correlation ID $refused_id)"
[ "$(runs "$refused_id")" = 0 ] || fail "an intake Logic App run started for Van Dijk Transport's refused booking"
pass "no intake Logic App run started for Van Dijk Transport's booking"

# 5. Application Insights. API Management and Logic Apps name themselves after their resource, and the TMS is tms-api
# (older telemetry reads [<environment>]/tms-api, after its Container Apps environment).
roles_with() {
  logs "union AppRequests, AppTraces, AppDependencies
| where TimeGenerated > ago(1h) and tostring(Properties) contains '$1'
| summarize by AppRoleName" | jq -r '.[][0]'
}
found() {
  local what=$1 value=$2 roles
  shift 2
  for _ in $(seq 1 30); do
    roles=$(roles_with "$value")
    local missing=
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
found "the accepted booking, by correlation ID," "$accepted_id" "$AZURE_ESTATE_API_MANAGEMENT" "$AZURE_ESTATE_INTAKE_LOGIC_APP" tms-api
found "the accepted booking, by Transport Order number," "$number" "$AZURE_ESTATE_INTAKE_LOGIC_APP"
found "Van Dijk Transport's refused booking, by correlation ID," "$refused_id" "$AZURE_ESTATE_API_MANAGEMENT"
if grep -qi "$AZURE_ESTATE_INTAKE_LOGIC_APP" <<<"$(roles_with "$refused_id")"; then
  fail "the intake Logic App traced Van Dijk Transport's refused booking"
fi
for correlation_id in "${refused_for_credentials[@]}"; do
  found "the call refused for its key or token ($correlation_id)" "$correlation_id" "$AZURE_ESTATE_API_MANAGEMENT"
done
