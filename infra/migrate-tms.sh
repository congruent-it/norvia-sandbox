#!/bin/sh
# azd postdeploy hook: applies the TMS database's migrations by starting the tms-migrations Container Apps job the
# AppHost deploys, and fails the deployment if they fail (coding guidelines §5: migrations are a deployment step).
set -eu

resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
execution=$(az containerapp job start --subscription "$AZURE_SUBSCRIPTION_ID" --name tms-migrations \
  --resource-group "$resource_group" --query name --output tsv)
echo "Migrating the TMS database ($execution)..."

# The job itself gives up after its replica timeout (30 minutes).
while
  status=$(az containerapp job execution show --subscription "$AZURE_SUBSCRIPTION_ID" --name tms-migrations \
    --resource-group "$resource_group" --job-execution-name "$execution" --query properties.status --output tsv)
  [ "$status" = Running ] || [ "$status" = Processing ]
do
  sleep 10
done

if [ "$status" != Succeeded ]; then
  echo "The TMS migrations ended $status. See the tms-migrations job's execution $execution in the portal." >&2
  exit 1
fi
echo "The TMS database is up to date."
