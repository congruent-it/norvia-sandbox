#!/bin/sh
# azd postdeploy hook: deploys the conversation sweeper's workflow to the Logic Apps Standard app the estate created. Unlike the other Logic
# Apps it needs no address from the apps Aspire deploys: it calls only Foundry, whose project endpoint the estate already gave it.
# The estate is at its 64 outputs, so the app is found by its tag instead of by an output.
set -eu
cd "$(dirname "$0")/.."

resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
logic_app=$(az resource list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" --resource-type Microsoft.Web/sites \
  --query "[?tags.workload=='conversation-sweeper'].name | [0]" --output tsv)
[ -n "$logic_app" ] || { echo "No conversation sweeper in $resource_group. Deploy the estate first: azd hooks run preprovision" >&2; exit 1; }

# The as-found toggle noConversationSweeper builds the app disabled (enabled: false), and a disabled app answers its deployment site with 403.
# That is the toggle's intent, so the workflow is left as it is, and the hook goes on to the other Logic Apps.
enabled=$(az resource show --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" --name "$logic_app" \
  --resource-type Microsoft.Web/sites --query properties.enabled --output tsv)
if [ "$enabled" = false ]; then
  echo "The conversation sweeper $logic_app is disabled (the as-found toggle noConversationSweeper), so its workflow is not deployed."
  exit 0
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
infra/stage-logic-app.sh src/Integration/ConversationSweeperLogicApp "$work/conversation-sweeper-logic-app.zip" host.json parameters.json connections.json conversation-sweeper

# A changed app setting restarts the app, and a zip deployment during the restart fails. Deploying again is safe.
for attempt in 1 2 3; do
  echo "Deploying the conversation sweeper to $logic_app (attempt $attempt)..."
  if az logicapp deployment source config-zip --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
    --src "$work/conversation-sweeper-logic-app.zip" --output none; then
    break
  fi
  [ "$attempt" -lt 3 ] || exit 1
  sleep 60
done
echo "The conversation sweeper deletes the conversations and responses of the Foundry project every night."
