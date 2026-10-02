#!/bin/sh
# azd postdeploy hook: deploys the Carrier-answer Logic App's workflow and local functions (P4.7) to the Logic Apps Standard app
# the estate created, and points it at the TMS API that Aspire just deployed. The estate can't do this itself: it is
# deployed before the TMS, whose address it doesn't know yet.
set -eu
cd "$(dirname "$0")/.."

resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
logic_app=${AZURE_ESTATE_ANSWER_LOGIC_APP:?Deploy the estate first: azd hooks run preprovision}

tms="https://$(az containerapp show --subscription "$AZURE_SUBSCRIPTION_ID" --name tms-api --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same address.
azd env set ESTATE_TMS_BASE_URL "$tms"
az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
  --settings "TMS_BASE_URL=$tms" --output none

# Building the local functions writes them into the Logic App project's lib/custom folder (ADR 0019).
dotnet build src/Integration/Norvia.Integration.CarrierAnswer.Functions --configuration Release
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
(cd src/Integration/AnswerLogicApp && zip -qr "$work/answer-logic-app.zip" host.json parameters.json carrier-answer lib)

# A changed app setting restarts the app, and a zip deployment during the restart fails. Deploying again is safe.
for attempt in 1 2 3; do
  echo "Deploying the Carrier-answer Logic App to $logic_app (attempt $attempt)..."
  if az logicapp deployment source config-zip --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
    --src "$work/answer-logic-app.zip" --output none; then
    break
  fi
  [ "$attempt" -lt 3 ] || exit 1
  sleep 60
done
echo "The Carrier-answer Logic App calls the TMS at $tms."
