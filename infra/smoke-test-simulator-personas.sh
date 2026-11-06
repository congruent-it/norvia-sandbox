#!/usr/bin/env bash
# Post-deploy presenter smoke run of the Simulator's personas agent in Azure (not in CI). The presenter previews a plan with the
# agent as the pool source and runs it, which the script can't do for them: Entra signs them in, and the Simulator's screens are theirs.
# The script stops and says what to do, reads Foundry and Application Insights, and checks. Run it in a terminal, signed in to the Azure
# CLI as the presenter the estate was provisioned for (the presenter holds the Foundry User role on the project), once `azd up` has
# deployed the estate and the apps and infra/deploy-agents.sh has published the Agent (it is the last postdeploy hook), with
# ESTATE_AGENTS_ENABLED_SETTING=true so the Simulator asks the Agent:
#
#   infra/smoke-test-simulator-personas.sh <run number>
#
# The run number is the one the Simulator's Configure screen says the next run gets. The presenter's steps (docs/operations/simulator.md,
# "Presenter smoke run in Azure"): choose Quiet Monday, set the seed to 349 and press Preview plan, then Start run and wait until it is
# Completed. The script checks that:
#   1. the Agent is published and has an identity of its own that holds no app role at all, and the project's shared identity holds none
#      (the Agent has no tool, so there is nothing for it to call);
#   2. the real model answers the Simulator's question with a list of texts, and the response is not kept by the service (store: false);
#   3. the generation is a span of the Simulator in Application Insights, with the Agent's name and a run ID, and its dependency on Foundry;
#   4. no text the model wrote is in any trace, span, request or dependency of the Simulator: the log names the profile and the category,
#      never a text. (Foundry's own trace keeps content apart, in AppGenAIContent; docs/operations/foundry.md, "Spike 2".)
# The presenter confirms two things by eye, which only the Simulator's screens show: the preview's Carrier texts say "agent, run N" as the
# source of their pools, and the Carriers' declines in the run's log are worded from those pools. It prints names, ids and counts only:
# never a token, a prompt or a text.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
endpoint=${FOUNDRY_PROJECT_ENDPOINT:-${AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT:-}}
agent=simulator-personas
run_number=${1:-}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $run_number =~ ^[0-9]+$ ]] || fail "usage: $0 <run number>, such as 1: the number the Configure screen gave the run"
[ -t 0 ] || fail "the presenter's steps are theirs, so run this in a terminal"
[ -n "$endpoint" ] || fail "no Foundry project endpoint: has the estate been deployed (azd provision)?"
[ "$(printf '%s' "${AZURE_ESTATE_AGENTS_ENABLED:-false}" | tr '[:upper:]' '[:lower:]')" = true ] \
  || fail "the Agents are off in this estate: deploy it with ESTATE_AGENTS_ENABLED_SETTING=true"

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
foundry_token() { az account get-access-token --resource https://ai.azure.com --query accessToken --output tsv; }
graph() { az rest --method get --url "https://graph.microsoft.com/v1.0$1" --output json; }

# 1. The Agent's identity holds no role; the project's shared identity holds none either.
published=$(curl -s --max-time 60 -H "Authorization: Bearer $(foundry_token)" "$endpoint/agents/$agent?api-version=v1")
identity_principal=$(jq -r '.instance_identity.principal_id // .identity.principal_id // empty' <<<"$published")
[ -n "$identity_principal" ] || fail "the Agent $agent is not published, or has no identity of its own: run infra/deploy-agents.sh"
held=$(graph "/servicePrincipals/$identity_principal/appRoleAssignments" | jq '.value | length')
[ "$held" = 0 ] || fail "the Agent's identity $identity_principal holds $held app role(s): it must hold none"
pass "the Agent has an identity of its own ($identity_principal) and it holds no app role"

account=$(az cognitiveservices account list --resource-group "$resource_group" --query "[?kind=='AIServices'].name | [0]" --output tsv)
project_identity=$(az resource show --resource-group "$resource_group" --resource-type Microsoft.CognitiveServices/accounts/projects \
  --name "$account/norvia" --query identity.principalId --output tsv)
if [ -n "$project_identity" ]; then
  [ "$(graph "/servicePrincipals/$project_identity/appRoleAssignments" | jq '.value | length')" = 0 ] || fail "the project's shared identity holds a role"
  pass "the project's shared identity holds no role"
fi

# 2. Ask, as the Simulator does: store nothing, cap the answer.
question=$'Write 5 texts.\nprofile: terse-dutch\nstyle: a few blunt words, no pleasantries\nlanguage: nl\ncategory: DeclineReason'
body=$(jq -n --arg agent "$agent" --arg question "$question" '{
  agent_reference: {type: "agent_reference", name: $agent},
  input: [{type: "message", role: "user", content: $question}],
  store: false, max_output_tokens: 800}')
response=$(curl -s --max-time 60 -X POST "$endpoint/openai/v1/responses" -H "Authorization: Bearer $(foundry_token)" -H 'Content-Type: application/json' -d "$body")
[ "$(jq -r '.status // empty' <<<"$response")" = completed ] || fail "the Agent's response did not complete: $(jq -c '.error // {status}' <<<"$response")"
jq -e '[.output[] | select(.type == "mcp_call" or .type == "function_call")] | length == 0' <<<"$response" >/dev/null || fail "the Agent called a tool, and it has none"
answer=$(jq -r '[.output[] | select(.type == "message") | .content[] | select(.type == "output_text") | .text] | join("")' <<<"$response" \
  | sed -e 's/^```json//' -e 's/^```//' -e 's/```$//')
texts=$(jq -c '.texts' <<<"$answer" 2>/dev/null || true)
jq -e 'type == "array" and length >= 3 and all(type == "string" and length > 0 and length < 120)' <<<"$texts" >/dev/null \
  || fail "the answer is not a list of at least three short texts"
pass "the real model wrote $(jq length <<<"$texts") texts"
response_id=$(jq -r .id <<<"$response")
status=$(curl -s -o /dev/null -w '%{http_code}' --max-time 60 -H "Authorization: Bearer $(foundry_token)" "$endpoint/openai/v1/responses/$response_id")
[ "$status" = 404 ] || [ "$status" = 400 ] || fail "the response $response_id can be read back (HTTP $status): store: false should keep nothing"
pass "the response is not kept by the service"

# The presenter's part: Preview plan with the agent as the pool source, then run it.
simulator="https://$(az containerapp show --name simulator-web --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
cat <<EOF2
As the presenter, in a browser, open the Simulator ($simulator) and sign in with Entra. Then:
  1. On Configure, check the number the next run gets is $run_number.
  2. Choose Quiet Monday, set the seed to 349 and Preview plan.
  3. Under Carrier texts, check every pool's source reads "agent, run $run_number" (a pool that fell back reads "built-in" and a note
     above says why).
  4. Start run and wait until it is Completed.
  5. In the log, check a Carrier's decline reason is worded like the pool's sample texts.
EOF2
read -r -p "Did the preview show the pools with source \"agent, run $run_number\"? [y/N] " answered
[ "$answered" = y ] || fail "the pools were not generated by the agent: see the note above the Carrier texts, and the Simulator's log in Application Insights"
read -r -p "Is the run Completed, and are the Carriers' declines drawn from the pools? [y/N] " answered
[ "$answered" = y ] || fail "the run is not done, or the Carriers' texts are not from the pools"

# 3. The generation is a span with the Agent's name and a run ID.
spans() {
  logs "union AppDependencies, AppRequests, AppTraces
| where TimeGenerated > ago(1d) and tostring(Properties['agent.name']) == '$agent' and isnotempty(tostring(Properties['agent.run.id']))
| summarize count()" | jq -r '.[0][0] // empty' | awk '$1 > 0'
}
count=$(until_found spans)
[ -n "$count" ] || fail "no 'Agent run' span of $agent with a run ID reached Application Insights in 10 minutes"
pass "$count span(s) of the Agent's runs are in Application Insights, each with its run ID"

# 4. No text is in the log. The texts the model wrote just now are looked for in every table the Simulator writes to.
leaks=0
while read -r text; do
  needle=$(jq -rn --arg t "$text" '$t | gsub("\\\\"; "\\\\\\\\") | gsub("\""; "\\\"")')
  found=$(logs "union AppTraces, AppDependencies, AppRequests, AppExceptions
| where TimeGenerated > ago(1d)
| where Message has \"$needle\" or Name has \"$needle\" or Data has \"$needle\" or tostring(Properties) has \"$needle\"
| count" | jq -r '.[0][0] // 0')
  leaks=$((leaks + found))
done < <(jq -r '.[]' <<<"$texts")
[ "$leaks" = 0 ] || fail "$leaks row(s) of Application Insights hold a text the model wrote: the log must name the profile and the category, never a text"
pass "none of the model's texts is in the traces, spans, requests, dependencies or exceptions"

echo "The Simulator's personas agent works in Azure."
