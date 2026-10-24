#!/usr/bin/env bash
# Post-deploy smoke run of the example Agent MCP server (not in CI). Run it in a terminal, signed in to the Azure CLI as the
# presenter the estate was provisioned for (the presenter owns the example identities' app registrations, because the deployment
# runs as them), once `azd up` has deployed the estate:
#
#   infra/smoke-test-agent-mcp-server.sh
#
# It calls the example server (contracts/agent-mcp/example.tools.json) through API Management with the example identity's token and
# checks that:
#   1. a call without a token, and a call with a token for another API, are refused with 401;
#   2. an identity without the read role cannot even get a token for the agent view;
#   3. tools/list with the example identity's token answers exactly the contract's tools;
#   4. the one tool answers, with the correlation ID API Management created;
#   5. a tool that is not on the list is refused with 403;
#   6. the call is in Application Insights by that correlation ID, with the Agent's identity (azp) and the tool name.
# The example identity has no secret, so the script adds a one-hour secret to it, uses it for the tokens and removes it again. It
# prints names and counts only: never a token or a secret.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
repo=$(cd "$(dirname "$0")/.." && pwd)
contract="$repo/contracts/agent-mcp/example.tools.json"
server=$(jq -r .server "$contract")
tenant=$(az account show --query tenantId --output tsv)
gateway=${AZURE_ESTATE_PARTNER_API_URL%/partner/v1}
mcp="$gateway/agents/$server/mcp"

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

app_id() { az ad app list --display-name "$1 ($AZURE_ENV_NAME)" --query '[0].appId' --output tsv; }
audience=$(app_id "Norvia example agent view")
caller=$(app_id "Norvia example agent identity")
stranger=$(app_id "Norvia example identity without the role")
[ -n "$audience" ] && [ -n "$caller" ] && [ -n "$stranger" ] || fail "the example identities are not in Entra: has the estate been deployed (azd provision)?"

secret_name="smoke-agent-mcp-$(date +%s)"
cleanup() {
  for app in "$caller" "$stranger"; do
    key=$(az ad app credential list --id "$app" --query "[?displayName=='$secret_name'].keyId | [0]" --output tsv 2>/dev/null || true)
    [ -z "$key" ] || az ad app credential delete --id "$app" --key-id "$key" --output none 2>/dev/null || true
  done
}
trap cleanup EXIT

secret_of() {
  az ad app credential reset --id "$1" --append --display-name "$secret_name" --end-date "$(date -u -d '+1 hour' +%F)" --query password --output tsv
}
# A new secret takes a little while to be usable (AADSTS7000215 until then). The answer is the token, or the Entra error code.
token_of() {
  local app=$1 secret=$2 answer code
  for _ in $(seq 1 12); do
    answer=$(curl -s -X POST "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token" \
      --data-urlencode "client_id=$app" --data-urlencode "client_secret=$secret" \
      --data-urlencode "scope=$audience/.default" --data-urlencode grant_type=client_credentials)
    if jq -e .access_token <<<"$answer" >/dev/null; then jq -r .access_token <<<"$answer"; return 0; fi
    code=$(jq -r '.error_description // ""' <<<"$answer" | grep -o 'AADSTS[0-9]*' | head -1 || true)
    [ "$code" = AADSTS7000215 ] || { echo "${code:-no-token}"; return 1; }
    sleep 10
  done
  echo AADSTS7000215
  return 1
}

echo "Example MCP server: $mcp"

# 1. No token, and a token for another API.
status=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$mcp" -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}')
[ "$status" = 401 ] || fail "a call without a token was answered $status, not 401"
arm_token=$(az account get-access-token --query accessToken --output tsv)
status=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$mcp" -H "Authorization: Bearer $arm_token" -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}')
[ "$status" = 401 ] || fail "a call with a token for another API was answered $status, not 401"
pass "a call without a token and a call with a token for another API are refused (401)"

# 2. An identity without the role gets no token for the view: Entra holds the view's service principal to assignment. (Were it to
# get one, the server would still refuse it with 403.)
stranger_secret=$(secret_of "$stranger")
if stranger_token=$(token_of "$stranger" "$stranger_secret"); then
  status=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$mcp" -H "Authorization: Bearer $stranger_token" -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}')
  [ "$status" = 403 ] || fail "an identity without the role got a token, and the server answered $status, not 403"
  pass "an identity without the read role is refused by the server (403)"
else
  [ "$stranger_token" = AADSTS501051 ] || fail "the identity without the role was refused with $stranger_token, not AADSTS501051 (not assigned to a role)"
  pass "an identity without the read role gets no token for the agent view ($stranger_token)"
fi

# The example identity.
caller_token=$(token_of "$caller" "$(secret_of "$caller")") || fail "the example identity got no token for the agent view: $caller_token"

# An MCP call: JSON-RPC over streamable HTTP. The answer is JSON or one server-sent event.
headers=$(mktemp)
mcp_call() {
  local body=$1
  curl -s -D "$headers" -X POST "$mcp" -H "Authorization: Bearer $caller_token" -H 'Content-Type: application/json' \
    -H 'Accept: application/json, text/event-stream' ${session:+-H "Mcp-Session-Id: $session"} -d "$body" | sed -n 's/^data: //p;/^[{[]/p'
}
session=""
mcp_call '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}' >/dev/null
session=$(grep -i '^mcp-session-id:' "$headers" | tr -d '\r' | cut -d' ' -f2 || true)
mcp_call '{"jsonrpc":"2.0","method":"notifications/initialized"}' >/dev/null

# 3. The tool list is the contract's.
listed=$(mcp_call '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' | jq -r '[.result.tools[].name] | sort | join(",")')
expected=$(jq -r '[.tools[].name] | sort | join(",")' "$contract")
[ "$listed" = "$expected" ] || fail "tools/list answered '$listed', the contract says '$expected'"
pass "tools/list answers the contract's tools: $listed"

# 4. The tool answers, and the answer carries the correlation ID.
tool=$(jq -r '.tools[0].name' "$contract")
answer=$(mcp_call "$(jq -nc --arg tool "$tool" '{jsonrpc:"2.0",id:3,method:"tools/call",params:{name:$tool,arguments:{}}}')")
jq -e '.result and (.result.isError | not)' <<<"$answer" >/dev/null || fail "the tool $tool did not answer: $(jq -c '.error // .result' <<<"$answer")"
correlation_id=$(grep -i '^x-correlation-id:' "$headers" | tr -d '\r' | cut -d' ' -f2 || true)
[ -n "$correlation_id" ] || fail "the answer carries no correlation ID"
pass "$tool answered (correlation ID $correlation_id)"

# 5. A tool that is not on the list.
status=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$mcp" -H "Authorization: Bearer $caller_token" -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' ${session:+-H "Mcp-Session-Id: $session"} \
  -d '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"delete_everything","arguments":{}}}')
[ "$status" = 403 ] || fail "a tool that is not on the list was answered $status, not 403"
pass "a tool that is not on the list is refused (403)"

# 6. The hop is in Application Insights with the Agent's identity and the tool name. Ingestion takes a few minutes: ask again for up to 10.
workspace=$(az monitor log-analytics workspace show --resource-group "$resource_group" \
  --workspace-name "$AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE" --query customerId --output tsv)
query="AppTraces | where TimeGenerated > ago(30m) | where tostring(Properties.correlationId) == '$correlation_id' | where tostring(Properties.agentId) == '$caller' and tostring(Properties.toolName) == '$tool' | count"
hops=0
for _ in $(seq 1 30); do
  hops=$(az rest --method post --resource https://api.loganalytics.io --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
    --body "$(jq -n --arg query "$query" '{query: $query, timespan: "PT1H"}')" --query 'tables[0].rows[0][0]' --output tsv)
  [ "${hops:-0}" -gt 0 ] && break
  sleep 20
done
[ "${hops:-0}" -gt 0 ] || fail "the call is not in Application Insights with agentId and toolName within 10 minutes"
pass "the call is on its Message trail with the Agent's identity and the tool name"
