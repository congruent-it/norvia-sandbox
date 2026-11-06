#!/usr/bin/env bash
# Post-deploy smoke run of the Estate health check in Azure (not in CI). Run it in a terminal, signed in to the Azure CLI as the presenter the
# estate was provisioned for (the presenter holds the Foundry User role on the project), once `azd up` has deployed the estate and the apps and
# infra/deploy-agents.ps1 has published the Agent (it is the last postdeploy hook), with ESTATE_AGENTS_ENABLED_SETTING=true so the page narrates:
#
#   infra/smoke-test-estate-health-check.sh
#
# It asks the Agent what the page's "Run health check" asks it (through the project, as the presenter; the page does it as its own identity), with
# the as-found alert toggle on and again with it off, and checks that:
#   1. the identities: the Agent has its own identity and holds only HealthCheck.Agent.Read, on the agent view and nothing else, and no Azure
#      role; the app's agent API has that one role, held by API Management's identity alone; the app's identity holds Reader on the estate's
#      resource group and no other role on it; the project's shared identity holds no role (#371, question 1);
#   2. with the as-found preset (entitiesWithoutDeadLetterAlert lists the queues and subscriptions without a dead-letter alert, and the other
#      toggles of the preset are on too) the checks return the Finding F-DLQ-ALERT among others, naming carrier-tracking, and the real model
#      explains it, citing the tool's result;
#   3. with the fixed preset, after the estate is deployed again, the alerts are back and the checks return no Finding: every Finding is gone.
#      The fixed estate has an alert receiver only when ESTATE_ALERT_EMAIL is set (azd env set), so set it first: without one the check
#      ALERT-RECEIVER rightly finds the alerts go to nobody;
#   4. the Agent's tool calls are hops of the run's Message trail, in API Management's MCP server and in the app, tagged with the Agent's identity
#      and the tool name, under the correlation ID of the run.
# The estate ends in the fixed state. Resource Graph takes a short while to see an alert come and go, so a check asks again, up to ten minutes. It
# prints names, ids and counts only: never a token, a prompt or an answer's text beyond a Finding's ID.
set -euo pipefail

cd "$(dirname "$0")"
eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
repo=$(cd .. && pwd)
endpoint=${FOUNDRY_PROJECT_ENDPOINT:-${AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT:-}}
contract="$repo/contracts/agent-mcp/health-check.tools.json"
agent=estate-health-check
role=$(jq -r .role "$contract")
correlation="smoke-$(date +%s)"

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

# 1. The identities and their roles.
published=$(curl -s --max-time 60 -H "Authorization: Bearer $(foundry_token)" "$endpoint/agents/$agent?api-version=v1")
identity_principal=$(jq -r '.instance_identity.principal_id // .identity.principal_id // empty' <<<"$published")
identity_client=$(jq -r '.instance_identity.client_id // .identity.client_id // empty' <<<"$published")
[ -n "$identity_principal" ] && [ -n "$identity_client" ] || fail "the Agent $agent is not published, or has no identity of its own: run infra/deploy-agents.ps1"
view=$(graph "/servicePrincipals?\$filter=displayName eq 'Norvia health-check agent view ($AZURE_ENV_NAME)'&\$select=id,appRoles")
view_id=$(jq -r '.value[0].id // empty' <<<"$view")
[ -n "$view_id" ] || fail "the agent view is not in Entra: has the estate been deployed?"
held=$(graph "/servicePrincipals/$identity_principal/appRoleAssignments" | jq -c '[.value[] | {resource: .resourceId, role: .appRoleId}]')
expected_role_id=$(jq -r --arg role "$role" '.value[0].appRoles[] | select(.value == $role) | .id' <<<"$view")
jq -e --arg view "$view_id" --arg role "$expected_role_id" 'length == 1 and .[0].resource == $view and .[0].role == $role' <<<"$held" >/dev/null \
  || fail "the Agent's identity does not hold exactly $role on the agent view: $held"
pass "the Agent's identity $identity_client holds $role on the agent view and nothing else"

azure_roles=$(az role assignment list --all --assignee "$identity_principal" --query 'length(@)' --output tsv)
[ "$azure_roles" = 0 ] || fail "the Agent's identity holds $azure_roles Azure role(s): it must hold none, Reader belongs to the app's identity"
pass "the Agent's identity holds no Azure role"

api=$(graph "/servicePrincipals?\$filter=displayName eq 'Norvia Estate health check agent API ($AZURE_ENV_NAME)'&\$select=id,appRoles")
api_id=$(jq -r '.value[0].id // empty' <<<"$api")
[ -n "$api_id" ] || fail "the app's agent API is not in Entra: has the estate been deployed?"
jq -e --arg role "$role" '[.value[0].appRoles[] | select(.isEnabled)] | length == 1 and .[0].value == $role' <<<"$api" >/dev/null \
  || fail "$role is not the only role on the app's agent API"
holders=$(graph "/servicePrincipals/$api_id/appRoleAssignedTo" | jq -r '[.value[].principalDisplayName] | join(",")')
case "$holders" in id-apim-*) ;; *) fail "the app's agent API is held by '$holders', not by API Management's identity alone" ;; esac
pass "$role is the only role on the app's agent API, and API Management's identity ($holders) alone holds it"

app_identity=$(az identity show --resource-group "$resource_group" --name "$AZURE_ESTATE_HEALTH_CHECK_IDENTITY_NAME" --query principalId --output tsv)
app_roles=$(az role assignment list --all --assignee "$app_identity" --query '[].{role: roleDefinitionName, scope: scope}' --output json)
rg_id=$(az group show --name "$resource_group" --query id --output tsv)
jq -e --arg rg "$rg_id" '[.[] | select(.scope | ascii_downcase | startswith($rg | ascii_downcase))] | map(.role) | sort | unique == ["Reader"]' <<<"$app_roles" >/dev/null \
  || fail "the app's identity holds other roles on the estate's resource group than Reader: $(jq -c '[.[].role]' <<<"$app_roles")"
pass "the app's identity holds Reader on the estate's resource group and no other role on it"

account=$(az cognitiveservices account list --resource-group "$resource_group" --query "[?kind=='AIServices'].name | [0]" --output tsv)
project_identity=$(az resource show --resource-group "$resource_group" --resource-type Microsoft.CognitiveServices/accounts/projects \
  --name "$account/norvia" --query identity.principalId --output tsv)
if [ -n "$project_identity" ]; then
  [ "$(graph "/servicePrincipals/$project_identity/appRoleAssignments" | jq '.value | length')" = 0 ] || fail "the project's shared identity holds a role"
  pass "the project's shared identity holds no role"
fi

# ask: asks the Agent what the page's button asks it, once. Prints the response; $correlation is the run's correlation ID.
ask() {
  local body
  body=$(jq -n --arg agent "$agent" --arg run "$correlation" '{
    agent_reference: {type: "agent_reference", name: $agent},
    input: [{type: "message", role: "user", content: "Run the estate health check and narrate its Findings."}],
    store: false, max_tool_calls: 2, max_output_tokens: 4000,
    structured_inputs: {correlationId: $run}}')
  curl -s --max-time 180 -X POST "$endpoint/openai/v1/responses" -H "Authorization: Bearer $(foundry_token)" -H 'Content-Type: application/json' -d "$body"
}

# wait_for_findings EXPECTED: asks until the checks return the Finding (EXPECTED=found) or none (EXPECTED=none), for up to ten minutes. Resource
# Graph is eventually consistent, so an alert that was just deleted or rebuilt is seen after a short while.
wait_for_findings() {
  local expected=$1 ids=""
  for _ in $(seq 1 20); do
    response=$(ask)
    if [ "$(jq -r '.status // empty' <<<"$response")" = completed ]; then
      ids=$(jq -r '[.output[] | select(.type == "mcp_call") | select(.name == "run_checks") | .output | fromjson | .findings[].id] | sort | join(",")' <<<"$response" 2>/dev/null || true)
      if [ "$expected" = found ] && [[ ",$ids," == *,F-DLQ-ALERT,* ]]; then return 0; fi
      if [ "$expected" = none ] && [ -z "$ids" ] && jq -e '[.output[] | select(.type == "mcp_call")] | length > 0' <<<"$response" >/dev/null; then return 0; fi
    fi
    sleep 30
  done
  fail "the checks did not return the expected Findings ($expected) within 10 minutes (last: '${ids:-none}')"
}

check_calls() {
  jq -e '[.output[] | select(.type == "mcp_call") | .name] | all(. == "run_checks")' <<<"$response" >/dev/null || fail "the Agent called a tool other than run_checks"
  jq -e '[.output[] | select(.type == "mcp_call") | select((.error // null) != null and .error != "" and .error != {})] | length == 0' <<<"$response" >/dev/null \
    || fail "a tool call was refused: $(jq -c '[.output[] | select(.type == "mcp_call") | {name, error}]' <<<"$response")"
}

# 2. The alert toggle on: the Finding appears.
./as-found.sh as-found
wait_for_findings found
check_calls
answer=$(jq -r '[.output[] | select(.type == "message") | .content[] | select(.type == "output_text") | .text] | join("")' <<<"$response")
jq -e '(.summary | length > 0) and ([.findings[] | select(.id == "F-DLQ-ALERT" and (.impact | length > 0) and .citation == "r1")] | length == 1)' <<<"$answer" >/dev/null \
  || fail "the narration does not explain F-DLQ-ALERT citing r1"
jq -e '[.output[] | select(.type == "mcp_call") | .output | fromjson | .findings[].evidence[].subject] | index("carrier-tracking")' <<<"$response" >/dev/null \
  || fail "the Finding's evidence does not name carrier-tracking"
pass "with the as-found preset on, the checks return F-DLQ-ALERT among others, naming carrier-tracking, and the real model explains it"
as_found_response=$(jq -r .id <<<"$response")

# 4 (first part). The hops: API Management's MCP server and the app, with the Agent's identity and the tool name.
hops() {
  logs "AppTraces | where TimeGenerated > ago(1h) | where tostring(Properties.runId) == '$correlation' | where tostring(Properties.flow) == '$1' | where tostring(Properties.agentId) == '$identity_client' and tostring(Properties.toolName) == 'run_checks' | count"
}
hops_pending=(agent-mcp-health-check agent-view-health-check)

# 3. The toggle off, the estate deployed again: the Finding is gone.
./as-found.sh fixed
wait_for_findings none
check_calls
answer=$(jq -r '[.output[] | select(.type == "message") | .content[] | select(.type == "output_text") | .text] | join("")' <<<"$response")
jq -e '.findings | length == 0' <<<"$answer" >/dev/null || fail "the narration explains a Finding although the checks returned none"
pass "with the alert toggle off and the estate deployed again, the checks return no Finding and the narration invents none"

# 4. Ingestion takes minutes: ask again for up to 10.
for flow in "${hops_pending[@]}"; do
  count=0
  for _ in $(seq 1 30); do
    count=$(hops "$flow")
    [ "${count:-0}" -gt 0 ] && break
    sleep 20
  done
  [ "${count:-0}" -gt 0 ] || fail "no hop of $flow on the run $correlation with the Agent's identity and run_checks within 10 minutes"
  pass "the Message trail has $count hop(s) of $flow, tagged with the Agent's identity and run_checks"
done

# Nothing kept: store was false. A response the service did keep is deleted.
for id in "$as_found_response" "$(jq -r .id <<<"$response")"; do
  kept=$(curl -s -o /dev/null -w '%{http_code}' --max-time 60 -H "Authorization: Bearer $(foundry_token)" "$endpoint/openai/v1/responses/$id")
  if [ "$kept" != 404 ]; then
    curl -s -o /dev/null -X DELETE --max-time 60 -H "Authorization: Bearer $(foundry_token)" "$endpoint/openai/v1/responses/$id" || true
    echo "NOTE  the service kept a response (HTTP $kept) although store was false; it was deleted. Record this in docs/operations/foundry.md (spike 3)."
  fi
done

health_check="https://$(az containerapp show --name health-check-web --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Done: the estate is in the fixed state. Open the page, choose Run health check, and see no Finding:"
echo "  $health_check/"
