#!/usr/bin/env bash
# Post-deploy smoke run of the conversation sweeper (not in CI). Run it in a terminal, signed in to the Azure CLI as the presenter the
# estate was provisioned for (the presenter holds the Foundry User role on the project and may manage the resource group), once `azd up`
# has deployed the estate and the postdeploy hooks have deployed the sweeper's workflow:
#
#   infra/smoke-test-conversation-sweeper.sh
#
# It leaves a conversation in the Foundry project, sets the sweeper's age to one minute, runs the sweeper once and checks that:
#   1. the sweeper's run succeeded, and
#   2. the conversation is gone.
# It restores the age (a day) when it ends, however it ends. It prints names, ids and counts only: never a token.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
endpoint=${FOUNDRY_PROJECT_ENDPOINT:-${AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT:-}}
workflow=conversation-sweeper
trigger=Every_night
default_age=1440

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[ -n "$endpoint" ] || fail "no Foundry project endpoint: has the estate been deployed (azd provision)?"
logic_app=$(az resource list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" --resource-type Microsoft.Web/sites \
  --tag workload=conversation-sweeper --query '[0].name' --output tsv)
[ -n "$logic_app" ] || fail "there is no conversation sweeper in $resource_group"
state=$(az webapp show --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" --name "$logic_app" --query state --output tsv)
[ "$state" = Running ] || fail "the sweeper $logic_app is $state, not Running (the as-found toggle noConversationSweeper is set?)"

set_age() {
  az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
    --settings "CONVERSATION_MAX_AGE_MINUTES=$1" --output none
}
trap 'set_age "$default_age"' EXIT

foundry_token() { az account get-access-token --resource https://ai.azure.com --query accessToken --output tsv; }
status_of_conversation() {
  curl -s -o /dev/null -w '%{http_code}' --max-time 60 -H "Authorization: Bearer $(foundry_token)" "$endpoint/openai/v1/conversations/$1"
}

# Leave a conversation behind.
created=$(curl -s --max-time 60 -X POST "$endpoint/openai/v1/conversations" -H "Authorization: Bearer $(foundry_token)" \
  -H 'Content-Type: application/json' -d '{"metadata":{"smoke":"conversation-sweeper"}}')
conversation=$(jq -r '.id // empty' <<<"$created")
[ -n "$conversation" ] || fail "could not create a conversation: does the presenter hold the Foundry User role?"
[ "$(status_of_conversation "$conversation")" = 200 ] || fail "the conversation $conversation cannot be read back"
pass "left the conversation $conversation in the project"

# A minute old, then sweep. A changed app setting restarts the app, so give it time before the trigger is run.
set_age 1
echo "Waiting 90 seconds for the conversation to be a minute old and the sweeper to restart..."
sleep 90

management="https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers/Microsoft.Web/sites/$logic_app/hostruntime/runtime/webhooks/workflow/api/management/workflows/$workflow"
started=$(date -u +%Y-%m-%dT%H:%M:%SZ)
for attempt in 1 2 3 4 5; do
  if az rest --method post --url "$management/triggers/$trigger/run?api-version=2018-11-01" --output none 2>/dev/null; then break; fi
  [ "$attempt" -lt 5 ] || fail "could not run the sweeper's trigger"
  sleep 30
done

# The run this call started: the newest run since the call.
run_status=
for _ in $(seq 1 30); do
  run_status=$(az rest --method get --url "$management/runs?api-version=2018-11-01" \
    --query "value[?properties.startTime >= '$started'] | [0].properties.status" --output tsv 2>/dev/null || true)
  case "$run_status" in Succeeded | Failed | Cancelled) break ;; esac
  sleep 10
done
[ "$run_status" = Succeeded ] || fail "the sweeper's run ended as '${run_status:-no run}', not Succeeded"
pass "the sweeper's run succeeded"

[ "$(status_of_conversation "$conversation")" = 404 ] || fail "the conversation $conversation is still there after the sweep"
pass "the conversation $conversation is gone"
