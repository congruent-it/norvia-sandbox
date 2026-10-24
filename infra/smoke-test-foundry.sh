#!/usr/bin/env bash
# Post-deploy smoke run of the Foundry estate (not in CI). Run it in a terminal, signed in to the Azure CLI as the presenter the
# estate was provisioned for (the presenter holds the Foundry User role on the project), once `azd up` has deployed the estate:
#
#   infra/smoke-test-foundry.sh
#
# It calls the shared model once through the project endpoint and checks that:
#   1. the model deployment is DataZoneStandard, in an EU region: processing stays in the EU Data Boundary, never GlobalStandard;
#   2. the deployment is capped and the account is tagged for cost;
#   3. the model answers one request;
#   4. Foundry's span for the call reaches Application Insights, and the prompt text (a sentinel the script made up) is nowhere in
#      the telemetry tables the Message trails use. The prompt content is allowed in the protected AppGenAIContent table only
#      (docs/operations/foundry.md, spike 2).
# It prints names and counts only: never a prompt or a token.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
endpoint=${FOUNDRY_PROJECT_ENDPOINT:-${AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT:-}}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[ -n "$endpoint" ] || fail "no Foundry project endpoint: has the estate been deployed (azd provision)?"
account=$(az cognitiveservices account list --resource-group "$resource_group" --query "[?kind=='AIServices'].name | [0]" --output tsv)
[ -n "$account" ] || fail "there is no Foundry account in $resource_group"

# 1 and 2. The deployment: type, region, cap, tags.
deployment=$(az cognitiveservices account deployment list --resource-group "$resource_group" --name "$account" --query '[0]' --output json)
[ "$deployment" != null ] || fail "the Foundry account has no model deployment (foundryModelDeployed is off?)"
model=$(jq -r .name <<<"$deployment")
sku=$(jq -r .sku.name <<<"$deployment")
capacity=$(jq -r .sku.capacity <<<"$deployment")
[ "$sku" = DataZoneStandard ] || fail "the deployment $model is $sku, not DataZoneStandard"
region=$(az cognitiveservices account show --resource-group "$resource_group" --name "$account" --query location --output tsv)
case "$region" in
  francecentral | germanywestcentral | italynorth | norwayeast | polandcentral | spaincentral | swedencentral | switzerlandnorth | westeurope) ;;
  *) fail "the Foundry account is in $region, which is not an EU region" ;;
esac
pass "the deployment $model is DataZoneStandard in $region"

[ "$capacity" -le 100 ] || fail "the deployment $model is capped at $capacity, not 100 or less"
tag=$(az cognitiveservices account show --resource-group "$resource_group" --name "$account" --query 'tags.workload' --output tsv)
[ "$tag" = foundry-agents ] || fail "the Foundry account has no workload cost tag"
pass "the deployment is capped at $capacity thousand tokens a minute, and the account is tagged for cost"

# 3. One request through the project endpoint, as the signed-in presenter.
sentinel="norvia-smoke-$(date +%s)-$RANDOM"
token=$(az account get-access-token --resource https://ai.azure.com --query accessToken --output tsv)
answer=$(curl -s -o /dev/null -w '%{http_code}' --max-time 120 -X POST "$endpoint/openai/v1/responses" \
  -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
  -d "$(jq -n --arg model "$model" --arg input "Reply with the single word pong. Reference $sentinel" '{model: $model, input: $input}')" || true)
[ "$answer" = 200 ] || fail "the model answered HTTP $answer, not 200: does the presenter hold the Foundry User role, and is the quota used up?"
pass "the model answered one request"

# 4. The span reached Application Insights, and the prompt text did not.
workspace=$(az monitor log-analytics workspace show --resource-group "$resource_group" \
  --workspace-name "$AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE" --query customerId --output tsv)
logs() {
  az rest --method post --resource https://api.loganalytics.io --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
    --body "$(jq -n --arg query "$1" '{query: $query, timespan: "PT1H"}')" --query 'tables[0].rows[0][0]' --output tsv
}
# Ingestion takes a few minutes, so the check asks again, for up to 10 minutes.
spans=0
for _ in $(seq 1 30); do
  spans=$(logs "union AppDependencies, AppTraces | where TimeGenerated > ago(30m) | where Properties has 'gen_ai' | count")
  [ "${spans:-0}" -gt 0 ] && break
  sleep 20
done
[ "${spans:-0}" -gt 0 ] || fail "no Foundry span reached Application Insights within 10 minutes: is the project connected to it?"
leaked=$(logs "union AppDependencies, AppTraces, AppEvents, AppRequests | where TimeGenerated > ago(30m) | where Properties has '$sentinel' or Name has '$sentinel' or Message has '$sentinel' | count")
[ "${leaked:-0}" -eq 0 ] || fail "the prompt text is in $leaked row(s) of the telemetry the Message trails use: content recording is on there"
pass "$spans Foundry span(s) reached Application Insights, and the prompt text is in none of the Message trail tables"
