#!/bin/sh
# azd postdeploy hook: deploys the P12 Driver reports Logic App's workflow to the Logic Apps Standard app the estate created, and
# points it at the TMS API and the Driver app that Aspire just deployed. The estate can't do this itself: it is deployed before
# them, and doesn't know their addresses yet.
set -eu
cd "$(dirname "$0")/.."

resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
logic_app=${AZURE_ESTATE_DRIVER_REPORTS_LOGIC_APP:?Deploy the estate first: azd hooks run preprovision}

fqdn() {
  az containerapp show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$1" --resource-group "$resource_group" \
    --query properties.configuration.ingress.fqdn --output tsv
}
tms="https://$(fqdn tms-api)"
driver_app="https://$(fqdn driver-app-api)"
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same addresses.
azd env set ESTATE_TMS_BASE_URL "$tms"
azd env set ESTATE_DRIVER_APP_BASE_URL "$driver_app"
az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
  --settings "TMS_BASE_URL=$tms" "DRIVER_APP_BASE_URL=$driver_app" --output none

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
infra/stage-logic-app.sh src/Integration/DriverReportsLogicApp "$work/driver-reports-logic-app.zip" host.json parameters.json connections.json driver-reports

# A changed app setting restarts the app, and a zip deployment during the restart fails. Deploying again is safe.
for attempt in 1 2 3; do
  echo "Deploying the Driver reports Logic App to $logic_app (attempt $attempt)..."
  if az logicapp deployment source config-zip --subscription "$AZURE_SUBSCRIPTION_ID" --name "$logic_app" --resource-group "$resource_group" \
    --src "$work/driver-reports-logic-app.zip" --output none; then
    break
  fi
  [ "$attempt" -lt 3 ] || exit 1
  sleep 60
done
echo "The Driver reports Logic App calls the TMS at $tms and the Driver app at $driver_app."
