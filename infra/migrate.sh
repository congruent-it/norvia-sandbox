#!/bin/sh
# azd postdeploy hook: applies a database's migrations by starting the migrations Container Apps job the AppHost
# deploys for it, and fails the deployment if they fail (coding guidelines §5: migrations are a deployment step).
# Usage: migrate.sh <job> <database>, such as: migrate.sh tms-migrations "the TMS database"
set -eu

job=$1
database=$2
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}

# A job can fail to start for reasons that pass, such as its managed identity not yet getting a token ("Failed to authenticate the
# user in Active Directory") or a serverless database still resuming. Migrations apply only what is pending, so a failed
# execution is started again.
for attempt in 1 2 3; do
  execution=$(az containerapp job start --subscription "$AZURE_SUBSCRIPTION_ID" --name "$job" \
    --resource-group "$resource_group" --query name --output tsv)
  echo "Migrating $database ($execution, attempt $attempt)..."

  # The job itself gives up after its replica timeout (30 minutes).
  while
    status=$(az containerapp job execution show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$job" \
      --resource-group "$resource_group" --job-execution-name "$execution" --query properties.status --output tsv)
    [ "$status" = Running ] || [ "$status" = Processing ]
  do
    sleep 10
  done

  [ "$status" != Succeeded ] || break
  echo "The migrations of $database ended $status. See the $job job's execution $execution in the portal." >&2
  [ "$attempt" -lt 3 ] || exit 1
  sleep 30
done
echo "$database is up to date."
