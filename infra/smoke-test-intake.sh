#!/usr/bin/env bash
# Post-deploy smoke test of Transport Order intake on the deployed TMS (not in CI). Run it after `azd up`, signed in to
# the Azure CLI as the presenter the estate was provisioned for. It checks that:
#   1. the holder of the TMS intake role (the intake Logic App's managed identity) submits a Transport Order and reads
#      it back, from a throwaway Container Apps job that runs as that identity;
#   2. a caller without the role is refused, by Entra and by the TMS;
#   3. the TransportOrderAccepted event is on the estate's transport-order-events topic;
#   4. the TMS's traces of the call are in Application Insights, found by correlation ID.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
correlation_id=$(cat /proc/sys/kernel/random/uuid)
shipper_reference="SMOKE-$(date +%Y%m%d%H%M%S)"
collection_day=$(date -u -d '+2 days' +%F)
booking=$(jq -c --arg reference "$shipper_reference" --arg day "$collection_day" '
  .shipperReference = $reference
  | .collection.window = { earliest: "\($day)T06:00:00+00:00", latest: "\($day)T10:00:00+00:00" }' \
  "$(dirname "$0")/../contracts/partner-api/samples/bookings/minimal.transport-order.json")

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

echo "TMS: $tms"
echo "Correlation ID: $correlation_id"

# 1. The intake identity submits and reads back. Its token can only be had inside Azure, so a job runs the calls.
job="smoke-intake-$(date +%s)"
trap 'az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true' EXIT
az containerapp job create --name "$job" --resource-group "$resource_group" \
  --environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
  --trigger-type Manual --replica-timeout 300 --replica-retry-limit 0 \
  --image mcr.microsoft.com/azure-cli:latest \
  --mi-user-assigned "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
  --env-vars "TMS=$tms" "TMS_API_CLIENT_ID=$AZURE_ESTATE_TMS_API_CLIENT_ID" "INTAKE_CLIENT_ID=$AZURE_ESTATE_INTAKE_CLIENT_ID" \
    "CORRELATION_ID=$correlation_id" "BOOKING=$booking" \
  --command /bin/sh --args -c '
    set -eu
    az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
    token=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
    status=$(curl -sS -o /tmp/submitted.json -w "%{http_code}" -X POST "$TMS/v1/transport-orders" \
      -H "Authorization: Bearer $token" -H "Content-Type: application/json" -H "X-Correlation-ID: $CORRELATION_ID" \
      -d "$BOOKING")
    echo "Submitted: $status $(cat /tmp/submitted.json)"
    [ "$status" = 201 ]
    number=$(python3 -c "import json; print(json.load(open(\"/tmp/submitted.json\"))[\"transportOrderNumber\"])")
    echo "Read back: $(curl -fsS "$TMS/v1/transport-orders/$number")"' \
  --output none
execution=$(az containerapp job start --name "$job" --resource-group "$resource_group" --query name --output tsv)
while
  status=$(az containerapp job execution show --name "$job" --resource-group "$resource_group" \
    --job-execution-name "$execution" --query properties.status --output tsv)
  [ "$status" = Running ] || [ "$status" = Processing ]
do
  sleep 10
done
[ "$status" = Succeeded ] || fail "the intake identity's submission ended $status; see the logs of job execution $execution"
pass "the intake identity submitted a Transport Order and read it back"

# 2. A caller without the role is refused: Entra won't issue it a token for the TMS, and the TMS refuses other tokens.
if az account get-access-token --resource "$AZURE_ESTATE_TMS_API_CLIENT_ID" --output none 2>/dev/null; then
  fail "Entra issued a TMS token to $(az account show --query user.name --output tsv), who doesn't hold the intake role"
fi
pass "Entra issues no TMS token to a caller without the intake role"
for token in "" "$(az account get-access-token --query accessToken --output tsv)"; do
  status=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$tms/v1/transport-orders" \
    ${token:+-H "Authorization: Bearer $token"} -H 'Content-Type: application/json' \
    -H "X-Correlation-ID: $(cat /proc/sys/kernel/random/uuid)" -d "$booking")
  [ "$status" = 401 ] || [ "$status" = 403 ] || fail "the TMS answered $status to a caller without the intake role"
done
pass "the TMS refuses a caller without a token and one with a token for another audience"

# 3. The event is on the topic. Peek-lock the monitoring subscription's messages, then release every lock.
service_bus_token=$(az account get-access-token --resource https://servicebus.azure.net --query accessToken --output tsv)
subscription="https://$AZURE_ESTATE_SERVICE_BUS_NAMESPACE.servicebus.windows.net/transport-order-events/subscriptions/monitoring"
locks=()
found=""
for _ in $(seq 1 50); do
  headers=$(curl -sS -D - -o /dev/null -X POST "$subscription/messages/head?timeout=10" -H "Authorization: Bearer $service_bus_token")
  grep -q '^HTTP/[0-9.]* 201' <<<"$headers" || break
  locks+=("$(grep -i '^location:' <<<"$headers" | tr -d '\r' | cut -d' ' -f2)")
  # The event type is the message's subject, which the REST API calls its label.
  broker_properties=$(grep -i '^brokerproperties:' <<<"$headers" | cut -d' ' -f2-)
  if jq -e --arg id "$correlation_id" '.CorrelationId == $id and .Label == "TransportOrderAccepted"' <<<"$broker_properties" >/dev/null; then
    found=yes
    break
  fi
done
for lock in "${locks[@]}"; do
  curl -sS -o /dev/null -X PUT "$lock" -H "Authorization: Bearer $service_bus_token"
done
[ -n "$found" ] || fail "no TransportOrderAccepted event with correlation ID $correlation_id on transport-order-events"
pass "TransportOrderAccepted is on the transport-order-events topic"

# 4. The TMS's traces are in Application Insights. Ingestion takes a few minutes.
workspace=$(az monitor log-analytics workspace show --resource-group "$resource_group" \
  --workspace-name "$AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE" --query customerId --output tsv)
query="let trail = AppTraces | where tostring(Properties.correlationId) == '$correlation_id' | distinct OperationId;
union AppRequests, AppTraces, AppDependencies
| where OperationId in (trail) and AppRoleName == 'tms-api'
| summarize count() by Type"
for _ in $(seq 1 30); do
  result=$(az rest --method post --resource https://api.loganalytics.io \
    --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
    --body "$(jq -n --arg query "$query" '{query: $query}')" \
    --query 'tables[0].rows' --output json)
  if jq -e 'map(.[0]) | index("AppRequests") and index("AppTraces")' <<<"$result" >/dev/null; then
    pass "the TMS's request and traces for the call are in Application Insights: $(jq -c . <<<"$result")"
    exit 0
  fi
  sleep 20
done
fail "no TMS request and traces with correlation ID $correlation_id in Application Insights after 10 minutes"
