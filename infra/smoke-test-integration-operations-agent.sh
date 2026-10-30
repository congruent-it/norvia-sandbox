#!/usr/bin/env bash
# Post-deploy smoke run of the Integration operations agent in Azure (not in CI). Run it in a terminal, signed in to the Azure CLI as the
# presenter the estate was provisioned for (the presenter holds the Foundry User role on the project), once `azd up` has deployed the estate
# and the apps and infra/deploy-agents.sh has published the Agent (it is the last postdeploy hook), with ESTATE_AGENTS_ENABLED_SETTING=true so
# the console shows Explain:
#
#   infra/smoke-test-integration-operations-agent.sh [<correlation ID of a trail with the unmappable code>]
#
# The trail is P5's unmappable code: the Carrier-tracking Logic App's run fails on a code outside its mapping table, such as DLV, and the
# message lands on the tracking queue's dead-letter queue (infra/smoke-test-shipper-visibility.sh sends one, and so does the Partner portal's
# presenter panel fault). Without an ID the script takes the latest such trail from Application Insights.
#
# It asks the Agent what the console's Explain button asks it (through the project, as the presenter, which the console does as its own
# identity) and checks that:
#   1. the Agent has its own identity and holds only IntegrationConsole.Agent.Read, on the agent view and nothing else; the console's agent API
#      has that one role, held by API Management's identity alone; the project's shared identity holds no role;
#   2. the real model answers with a Suggestion that names the cause, cites a result, and has called only get_message_trail and get_run;
#   3. the Agent's tool calls are hops of the Message trail, in API Management's MCP server and in the console, tagged with the Agent's
#      identity and the tool name, under the same correlation ID as the trail;
#   4. the response is not kept by the service (store: false), or is gone once it is deleted.
# The Explain button itself is pressed by hand: the script prints the console address of the trail. It prints names, ids and counts only: never
# a token, a prompt or an answer's text beyond the cause.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
repo=$(cd "$(dirname "$0")/.." && pwd)
endpoint=${FOUNDRY_PROJECT_ENDPOINT:-${AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT:-}}
contract="$repo/contracts/agent-mcp/integration-operations.tools.json"
agent=integration-operations
role=$(jq -r .role "$contract")
trail=${1:-}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[ -n "$endpoint" ] || fail "no Foundry project endpoint: has the estate been deployed (azd provision)?"
workspace=$(az monitor log-analytics workspace show --resource-group "$resource_group" \
  --workspace-name "$AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE" --query customerId --output tsv)
logs() {
  az rest --method post --resource https://api.loganalytics.io --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
    --body "$(jq -n --arg query "$1" '{query: $query, timespan: "P1D"}')" --query 'tables[0].rows[0][0]' --output tsv
}
foundry_token() { az account get-access-token --resource https://ai.azure.com --query accessToken --output tsv; }
graph() { az rest --method get --url "https://graph.microsoft.com/v1.0$1" --output json; }

if [ -z "$trail" ]; then
  trail=$(logs "union AppTraces, AppRequests, AppDependencies | where TimeGenerated > ago(1d) | where tostring(Properties.failureCode) == 'UnmappableTrackingMessage' | top 1 by TimeGenerated desc | project tostring(Properties.correlationId)")
  [ -n "$trail" ] || fail "no trail with the unmappable code in the last day: send a Carrier's DLV first (infra/smoke-test-shipper-visibility.sh, or the Partner portal's presenter panel)"
fi
echo "Trail: $trail"

# 1. The identities and their roles.
published=$(curl -s --max-time 60 -H "Authorization: Bearer $(foundry_token)" "$endpoint/agents/$agent?api-version=v1")
identity_principal=$(jq -r '.instance_identity.principal_id // .identity.principal_id // empty' <<<"$published")
identity_client=$(jq -r '.instance_identity.client_id // .identity.client_id // empty' <<<"$published")
[ -n "$identity_principal" ] && [ -n "$identity_client" ] || fail "the Agent $agent is not published, or has no identity of its own: run infra/deploy-agents.sh"
view=$(graph "/servicePrincipals?\$filter=displayName eq 'Norvia $agent agent view ($AZURE_ENV_NAME)'&\$select=id,appRoles")
view_id=$(jq -r '.value[0].id // empty' <<<"$view")
[ -n "$view_id" ] || fail "the agent view is not in Entra: has the estate been deployed?"
held=$(graph "/servicePrincipals/$identity_principal/appRoleAssignments" | jq -c '[.value[] | {resource: .resourceId, role: .appRoleId}]')
expected_role_id=$(jq -r --arg role "$role" '.value[0].appRoles[] | select(.value == $role) | .id' <<<"$view")
jq -e --arg view "$view_id" --arg role "$expected_role_id" 'length == 1 and .[0].resource == $view and .[0].role == $role' <<<"$held" >/dev/null \
  || fail "the Agent's identity does not hold exactly $role on the agent view: $held"
pass "the Agent's identity $identity_client holds $role on the agent view and nothing else"

api=$(graph "/servicePrincipals?\$filter=displayName eq 'Norvia Integration console agent API ($AZURE_ENV_NAME)'&\$select=id,appRoles")
api_id=$(jq -r '.value[0].id // empty' <<<"$api")
[ -n "$api_id" ] || fail "the console's agent API is not in Entra: has the estate been deployed?"
jq -e --arg role "$role" '[.value[0].appRoles[] | select(.isEnabled)] | length == 1 and .[0].value == $role' <<<"$api" >/dev/null \
  || fail "$role is not the only role on the console's agent API"
holders=$(graph "/servicePrincipals/$api_id/appRoleAssignedTo" | jq -r '[.value[].principalDisplayName] | join(",")')
case "$holders" in id-apim-*) ;; *) fail "the console's agent API is held by '$holders', not by API Management's identity alone" ;; esac
pass "$role is the only role on the console's agent API, and API Management's identity ($holders) alone holds it"

account=$(az cognitiveservices account list --resource-group "$resource_group" --query "[?kind=='AIServices'].name | [0]" --output tsv)
project_identity=$(az resource show --resource-group "$resource_group" --resource-type Microsoft.CognitiveServices/accounts/projects \
  --name "$account/norvia" --query identity.principalId --output tsv)
if [ -n "$project_identity" ]; then
  [ "$(graph "/servicePrincipals/$project_identity/appRoleAssignments" | jq '.value | length')" = 0 ] || fail "the project's shared identity holds a role"
  pass "the project's shared identity holds no role"
fi

# 2. Ask, as Explain does.
question="Explain why the Message trail $trail failed."
body=$(jq -n --arg agent "$agent" --arg question "$question" --arg trail "$trail" '{
  agent_reference: {type: "agent_reference", name: $agent},
  input: [{type: "message", role: "user", content: $question}],
  store: false, max_tool_calls: 6, max_output_tokens: 1200,
  structured_inputs: {correlationId: $trail}}')
response=$(curl -s --max-time 180 -X POST "$endpoint/openai/v1/responses" -H "Authorization: Bearer $(foundry_token)" -H 'Content-Type: application/json' -d "$body")
[ "$(jq -r '.status // empty' <<<"$response")" = completed ] || fail "the Agent's response did not complete: $(jq -c '.error // {status}' <<<"$response")"
tools=$(jq -r '[.output[] | select(.type == "mcp_call") | .name] | unique | join(",")' <<<"$response")
[ -n "$tools" ] || fail "the Agent called no tool"
jq -e --slurpfile contract "$contract" '[.output[] | select(.type == "mcp_call") | .name] | all(. as $n | $contract[0].tools | map(.name) | index($n))' <<<"$response" >/dev/null \
  || fail "the Agent called a tool that is not on the contract: $tools"
jq -e '[.output[] | select(.type == "mcp_call") | select((.error // null) != null and .error != "" and .error != {})] | length == 0' <<<"$response" >/dev/null \
  || fail "a tool call was refused: $(jq -c '[.output[] | select(.type == "mcp_call") | {name, error}]' <<<"$response")"
jq -e '[.output[] | select(.type == "mcp_call") | .name] | index("get_message_trail")' <<<"$response" >/dev/null || fail "the Agent did not read the Message trail"
answer=$(jq -r '[.output[] | select(.type == "message") | .content[] | select(.type == "output_text") | .text] | join("")' <<<"$response")
jq -e '(.cause | length > 0) and (.evidence | length > 0) and (.suggestedAction | length > 0) and (.confidence | IN("low","medium","high"))' <<<"$answer" >/dev/null \
  || fail "the answer is not a Suggestion"
cause=$(jq -r .cause <<<"$answer")
grep -qiE 'DLV|map|unmapp|status code|tracking code' <<<"$cause" || fail "the cause does not name the unmappable code: $cause"
pass "the real model answered; tools called: $tools"
echo "      Cause: $cause"

# 3. The hops: API Management's MCP server and the console, with the Agent's identity and the tool name. Ingestion takes minutes: ask again for up to 10.
hops() {
  logs "AppTraces | where TimeGenerated > ago(1h) | where tostring(Properties.runId) == '$trail' | where tostring(Properties.flow) == '$1' | where tostring(Properties.agentId) == '$identity_client' and tostring(Properties.toolName) == 'get_message_trail' | count"
}
for flow in agent-mcp-integration-operations agent-view-integration-operations; do
  count=0
  for _ in $(seq 1 30); do
    count=$(hops "$flow")
    [ "${count:-0}" -gt 0 ] && break
    sleep 20
  done
  [ "${count:-0}" -gt 0 ] || fail "no hop of $flow on the trail $trail with the Agent's identity and get_message_trail within 10 minutes"
  pass "the Message trail has $count hop(s) of $flow, tagged with the Agent's identity and get_message_trail"
done

# 4. Nothing kept.
response_id=$(jq -r .id <<<"$response")
kept=$(curl -s -o /dev/null -w '%{http_code}' --max-time 60 -H "Authorization: Bearer $(foundry_token)" "$endpoint/openai/v1/responses/$response_id")
if [ "$kept" = 404 ]; then
  pass "the service did not keep the response (store: false is honoured when the Agent calls an MCP tool)"
else
  curl -s -o /dev/null -X DELETE --max-time 60 -H "Authorization: Bearer $(foundry_token)" "$endpoint/openai/v1/responses/$response_id" || true
  echo "NOTE  the service kept the response (HTTP $kept) although store was false; it was deleted. Record this in docs/operations/foundry.md (spike 3)."
fi

console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Open in the Integration console, choose Explain, and see the same answer and the Agent's hops on the trail:"
echo "  $console/?key=$trail"
