#!/bin/sh
# azd postdeploy hook: deploys the E1 Billing Logic App's workflow to the Logic Apps Standard app the estate created, and points it at
# the ERP billing API that Aspire just deployed. The estate can't do this itself: it is deployed before the ERP, and doesn't know its
# address yet.
set -eu
cd "$(dirname "$0")/.."

resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
logic_app=${AZURE_ESTATE_BILLING_LOGIC_APP:?Deploy the estate first: azd hooks run preprovision}

erp="https://$(az containerapp show --subscription "$AZURE_SUBSCRIPTION_ID" --name erp-api --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same address.
azd env set ESTATE_ERP_BASE_URL "$erp"
az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
  --settings "ERP_BASE_URL=$erp" --output none

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
(cd src/Integration/BillingLogicApp && zip -qr "$work/billing-logic-app.zip" host.json parameters.json connections.json billing)

# A changed app setting restarts the app, and a zip deployment during the restart fails. Deploying again is safe.
for attempt in 1 2 3; do
  echo "Deploying the Billing Logic App to $logic_app (attempt $attempt)..."
  if az logicapp deployment source config-zip --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
    --src "$work/billing-logic-app.zip" --output none; then
    break
  fi
  [ "$attempt" -lt 3 ] || exit 1
  sleep 60
done
echo "The Billing Logic App records Billable lines with the ERP billing API at $erp."
