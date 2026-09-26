#!/usr/bin/env bash
# Post-deploy presenter smoke run of the Integration console, slice 1 (not in CI). Book a Transport Order as Brouwer
# Dranken in the Partner portal first, then run it, signed in to the Azure CLI as the presenter the estate was
# provisioned for, with the Transport Order number the portal showed and, optionally, Norvia's reference for the
# attempt under it, which is the booking's correlation ID:
#
#   infra/smoke-test-integration-console.sh TO-0000042 [<correlation ID>]
#
# Without a number it takes Brouwer Dranken's latest booking of the last hour. It checks, in the sources the console
# reads, that:
#   1. the booking's Message trail is found by its Transport Order number, and by its correlation ID, as one trail of
#      the flow transport-order-intake that succeeded;
#   2. the trail shows the API Management call, the intake Logic App run and the TMS call, with the same correlation ID;
#   3. the intake run's steps open, and the map's and the TMS call's input and output bodies can be fetched.
# It reads as the presenter, not as the console's identity, so the console's own access is seen only on screen. It then
# prints the console's pages to open. It never prints a body: they hold the Consignee's personal data. The presenter's
# steps in the Partner portal and the console are in docs/operations/integration-console.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
transport_order_number=${1:-}
shown_correlation_id=${2:-}
brouwer_dranken_id=SHP-0001

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

# Both go into the queries below, so they must be what they claim to be.
[ -z "$transport_order_number" ] || [[ $transport_order_number =~ ^TO-[0-9]+$ ]] \
  || fail "$transport_order_number isn't a Transport Order number, such as TO-0000042"
[ -z "$shown_correlation_id" ] || [[ $shown_correlation_id =~ ^[0-9a-fA-F-]{36}$ ]] \
  || fail "$shown_correlation_id isn't a correlation ID"

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

# The telemetry rows with the tracking contract's keys (coding guidelines §7), read where each source records them, as
# the console reads them: applications straight in Properties, API Management also as its logged X-Correlation-ID
# header, and Logic Apps as tracked properties, a JSON string in Properties.trackedProperties.
tracked_rows='union AppRequests, AppTraces, AppDependencies
| extend tracked = parse_json(tostring(Properties.trackedProperties))
| extend correlationId = coalesce(tostring(Properties.correlationId), tostring(tracked.correlationId),
    tostring(Properties["Response-X-Correlation-ID"]), tostring(Properties["Request-X-Correlation-ID"])),
  flow = coalesce(tostring(Properties.flow), tostring(tracked.flow)),
  outcome = coalesce(tostring(Properties.outcome), tostring(tracked.outcome)),
  transportOrderNumber = coalesce(tostring(Properties.transportOrderNumber), tostring(tracked.transportOrderNumber)),
  partyId = coalesce(tostring(Properties.partyId), tostring(tracked.partyId)),
  runId = tostring(parse_json(tostring(Properties.resource)).runId)
| where isnotempty(correlationId)'

if [ -z "$transport_order_number" ]; then
  transport_order_number=$(logs "$tracked_rows
| where TimeGenerated > ago(1h) and AppRoleName == '$AZURE_ESTATE_INTAKE_LOGIC_APP' and partyId == '$brouwer_dranken_id'
    and isnotempty(transportOrderNumber)
| top 1 by TimeGenerated desc | project transportOrderNumber" | jq -r '.[0][0] // empty')
  [ -n "$transport_order_number" ] \
    || fail "Brouwer Dranken booked nothing in the last hour, or it isn't in Application Insights yet; pass its Transport Order number"
fi
echo "Transport Order: $transport_order_number"

# 1. Found by the Transport Order number, and by the correlation ID, as one trail.
trails_carrying() {
  logs "$tracked_rows | where Properties has '$1'
| where correlationId =~ '$1' or transportOrderNumber =~ '$1'
| summarize by correlationId" | jq -r '.[][0]'
}
correlation_ids=$(until_found trails_carrying "$transport_order_number")
[ -n "$correlation_ids" ] || fail "no Message trail carries Transport Order $transport_order_number after 10 minutes"
[ "$(wc -l <<<"$correlation_ids")" = 1 ] \
  || fail "Transport Order $transport_order_number is on more than one trail: $(tr '\n' ' ' <<<"$correlation_ids")"
correlation_id=$correlation_ids
pass "the Message trail is found by Transport Order $transport_order_number (correlation ID $correlation_id)"
[ -z "$shown_correlation_id" ] || [ "${shown_correlation_id,,}" = "${correlation_id,,}" ] \
  || fail "the trail's correlation ID is $correlation_id, not $shown_correlation_id, the reference the Partner portal showed"

# As the console's search sums up a trail: its business keys, flows and outcomes.
trail=$(logs "$tracked_rows | where Properties has '$correlation_id' and correlationId =~ '$correlation_id'
| summarize transportOrderNumbers = make_set_if(transportOrderNumber, isnotempty(transportOrderNumber)),
    flows = make_set_if(flow, isnotempty(flow)), outcomes = make_set_if(outcome, isnotempty(outcome))" \
  | jq -c '.[0] // empty | {transportOrderNumbers: (.[0] | fromjson), flows: (.[1] | fromjson), outcomes: (.[2] | fromjson)}')
jq -e --arg number "$transport_order_number" '.transportOrderNumbers | any(. == $number)' <<<"$trail" >/dev/null \
  || fail "the trail with correlation ID $correlation_id doesn't carry Transport Order $transport_order_number"
pass "the Message trail is found by its correlation ID, and carries Transport Order $transport_order_number"
jq -e '.flows == ["transport-order-intake"] and .outcomes == ["Succeeded"]' <<<"$trail" >/dev/null \
  || fail "the trail isn't one successful transport-order-intake: $trail"
pass "the trail's flow is transport-order-intake, and it succeeded"

# 2. The API Management call, the intake run and the TMS call, each under the trail's correlation ID. API Management and
# Logic Apps name themselves after their resource; the TMS after its Container Apps environment too: [<environment>]/tms-api.
hops() {
  logs "$tracked_rows | where Properties has '$correlation_id' and correlationId =~ '$correlation_id'
| where Type == 'AppRequests' or isnotempty(runId)
| summarize runIds = make_set_if(runId, isnotempty(runId)), parties = make_set_if(partyId, isnotempty(partyId)) by AppRoleName" \
    | jq -c 'map({role: .[0], runIds: (.[1] | fromjson), parties: (.[2] | fromjson)})'
}
hop_of() {
  jq -c --arg role "$2" '[.[] | select(.role | ascii_downcase | contains($role | ascii_downcase))] | first // empty' <<<"$1"
}
every_hop() {
  local found
  found=$(hops)
  [ -z "$(hop_of "$found" "$AZURE_ESTATE_API_MANAGEMENT")" ] || [ -z "$(hop_of "$found" "$AZURE_ESTATE_INTAKE_LOGIC_APP")" ] \
    || [ -z "$(hop_of "$found" tms-api)" ] || echo "$found"
}
hops_found=$(until_found every_hop)
# After 10 minutes, say which hop is missing.
[ -n "$hops_found" ] || hops_found=$(hops)
[ -n "$(hop_of "$hops_found" "$AZURE_ESTATE_API_MANAGEMENT")" ] || fail "the trail has no API Management call after 10 minutes"
pass "the trail shows the API Management call"
intake=$(hop_of "$hops_found" "$AZURE_ESTATE_INTAKE_LOGIC_APP")
[ -n "$intake" ] || fail "the trail has no intake Logic App run after 10 minutes"
[ "$(jq '.runIds | length' <<<"$intake")" = 1 ] || fail "the trail has $(jq '.runIds | length' <<<"$intake") intake runs, not one"
run_id=$(jq -r '.runIds[0]' <<<"$intake")
jq -e --arg party "$brouwer_dranken_id" '.parties == [$party]' <<<"$intake" >/dev/null \
  || fail "the intake run tracked the Shipper $(jq -c '.parties' <<<"$intake"), not Brouwer Dranken ($brouwer_dranken_id)"
pass "the trail shows the intake Logic App run $run_id, for Brouwer Dranken ($brouwer_dranken_id)"
[ -n "$(hop_of "$hops_found" tms-api)" ] || fail "the trail has no TMS call after 10 minutes"
pass "the trail shows the TMS call"

# 3. The run's steps and their bodies, live from the Logic Apps management API, as the console fetches them.
workflow=transport-order-intake
run_address="https://management.azure.com$AZURE_ESTATE_RESOURCE_GROUP_ID/providers/Microsoft.Web/sites/$AZURE_ESTATE_INTAKE_LOGIC_APP/hostruntime/runtime/webhooks/workflow/api/management/workflows/$workflow/runs/$run_id"
steps=$(az rest --method get --url "$run_address/actions?api-version=2018-11-01" --output json)
jq -e '.value | length > 0' <<<"$steps" >/dev/null || fail "run $run_id has no steps; has run history passed its retention?"
pass "run $run_id opens with its steps: $(jq -r '[.value[] | "\(.name) \(.properties.status)"] | join(", ")' <<<"$steps")"
body_fetched() {
  local link
  link=$(jq -r --arg step "$1" --arg link "$2" '.value[] | select(.name == $step) | .properties[$link].uri // empty' <<<"$steps")
  [ -n "$link" ] && [ "$(curl -sS -o /dev/null -w '%{http_code}' "$link")" = 200 ]
}
for step in Map_the_booking Submit_to_the_TMS; do
  body_fetched "$step" inputsLink || fail "$step's input body can't be fetched"
  body_fetched "$step" outputsLink || fail "$step's output body can't be fetched"
  pass "$step opens with its input and output bodies"
done

console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Open in the Integration console:"
echo "  search:  $console/?key=$transport_order_number"
echo "  trail:   $console/trails/$correlation_id"
echo "  run:     $console/runs/$AZURE_ESTATE_INTAKE_LOGIC_APP/$workflow/$run_id"
