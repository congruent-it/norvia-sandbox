#!/bin/sh
# azd postdeploy hook: applies a database's migrations by starting the migrations Container Apps job the AppHost
# deploys for it, and fails the deployment if they fail (coding guidelines §5: migrations are a deployment step).
# Usage: migrate.sh <job> <database>, such as: migrate.sh tms-migrations "the TMS database"
set -eu

job=$1
database=$2
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
execution=$(az containerapp job start --subscription "$AZURE_SUBSCRIPTION_ID" --name "$job" \
  --resource-group "$resource_group" --query name --output tsv)
echo "Migrating $database ($execution)..."

# The job itself gives up after its replica timeout (30 minutes).
while
  status=$(az containerapp job execution show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$job" \
    --resource-group "$resource_group" --job-execution-name "$execution" --query properties.status --output tsv)
  [ "$status" = Running ] || [ "$status" = Processing ]
do
  sleep 10
done

if [ "$status" != Succeeded ]; then
  echo "The migrations of $database ended $status. See the $job job's execution $execution in the portal." >&2
  exit 1
fi
echo "$database is up to date."
