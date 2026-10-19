#!/bin/sh
# azd postdeploy hook: deploys the E2 Carrier invoice outcome Logic App's workflow and local function (E2.8) to the Logic Apps Standard app the
# estate created, and points it at the ERP billing API and the Partner portal that Aspire just deployed. The estate can't do this
# itself: it is deployed before them, and doesn't know their addresses yet.
set -eu
cd "$(dirname "$0")/.."

resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
# The estate has no output left for it (Bicep allows 64), so the app is found by its name.
logic_app=$(az logicapp list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" \
  --query "[?starts_with(name, 'logic-carrier-invoice-outcome-')].name | [0]" --output tsv)
[ -n "$logic_app" ] || { echo "Deploy the estate first: azd hooks run preprovision" >&2; exit 1; }

fqdn() {
  az containerapp show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$1" --resource-group "$resource_group" \
    --query properties.configuration.ingress.fqdn --output tsv
}
erp="https://$(fqdn erp-api)"
portal="https://$(fqdn partner-portal-web)"
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same addresses.
azd env set ESTATE_ERP_BASE_URL "$erp"
azd env set ESTATE_PARTNER_PORTAL_BASE_URL "$portal"
az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
  --settings "ERP_BASE_URL=$erp" "PARTNER_PORTAL_BASE_URL=$portal" --output none

# Building the local function writes it into the Logic App project's lib/custom folder (ADR 0019).
dotnet build src/Integration/Norvia.Integration.CarrierInvoiceOutcome.Functions --configuration Release
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
(cd src/Integration/CarrierInvoiceOutcomeLogicApp && zip -qr "$work/carrier-invoice-outcome-logic-app.zip" host.json parameters.json connections.json carrier-invoice-outcome lib)

# A changed app setting restarts the app, and a zip deployment during the restart fails. Deploying again is safe.
for attempt in 1 2 3; do
  echo "Deploying the Carrier invoice outcome Logic App to $logic_app (attempt $attempt)..."
  if az logicapp deployment source config-zip --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
    --src "$work/carrier-invoice-outcome-logic-app.zip" --output none; then
    break
  fi
  [ "$attempt" -lt 3 ] || exit 1
  sleep 60
done
echo "The Carrier invoice outcome Logic App calls the ERP at $erp and the Carriers' endpoints at $portal."
