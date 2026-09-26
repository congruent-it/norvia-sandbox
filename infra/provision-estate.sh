#!/bin/sh
# azd preup and preprovision hook: deploys the estate Bicep (ADR 0004), then stores its outputs in the azd environment
# where azd looks up the AppHost's Estate parameters when it provisions and deploys the apps. It runs before
# `azd up` too, because azd asks for those parameters before it runs the preprovision hook; `azd provision` on its own
# runs it as preprovision.
#
# It deploys with the Azure CLI, which must be signed in to the same tenant as azd, as a user who may create app
# registrations (the Entra ones are part of the estate).
set -eu
cd "$(dirname "$0")"

: "${AZURE_SUBSCRIPTION_ID:?Choose a subscription first: azd env set AZURE_SUBSCRIPTION_ID <id>}"
: "${AZURE_LOCATION:?Choose a region first: azd env set AZURE_LOCATION <region>}"

# In one `azd up` this runs twice, as preup and as preprovision, both times under the same azd process. The second
# time the estate is already deployed.
deployed_in_this_run="${TMPDIR:-/tmp}/norvia-estate-$AZURE_ENV_NAME-$PPID"
if [ -n "$(find "$deployed_in_this_run" -mmin -180 2>/dev/null)" ]; then
  echo "The Norvia estate is already deployed in this run of azd."
  exit 0
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

ESTATE_PRESENTER_PRINCIPAL_ID=$(az ad signed-in-user show --query id --output tsv 2>/dev/null || true)
export ESTATE_PRESENTER_PRINCIPAL_ID

# A new managed identity can take a minute to reach Microsoft Graph, which then refuses to grant it an app role.
# Deploying again is safe, so a first attempt that fails is retried.
for attempt in 1 2 3; do
  echo "Deploying the Norvia estate to rg-$AZURE_ENV_NAME (attempt $attempt)..."
  if az deployment sub create \
    --subscription "$AZURE_SUBSCRIPTION_ID" \
    --name "norvia-estate-$AZURE_ENV_NAME" \
    --location "$AZURE_LOCATION" \
    --parameters estate.bicepparam \
    --query properties.outputs --output json > "$work/outputs.json"; then
    break
  fi
  [ "$attempt" -lt 3 ] || exit 1
  sleep 60
done

# Each output as its name, its value as a JSON string, and its value as it is, tab-separated.
jq -r 'to_entries[] | [(.key | ascii_upcase), (.value.value | tostring | tojson), (.value.value | tostring)] | @tsv' \
  "$work/outputs.json" > "$work/outputs.env"
# Provisioning finds an Estate parameter as AZURE_<NAME> in the azd environment, but deploying, which puts some of them
# straight into the apps' settings, reads them only from the environment's config as infra.parameters.<name>. That
# config parses what it is given as JSON, and deploying accepts only strings, so a number such as the retention goes in
# quoted.
tab=$(printf '\t')
while IFS="$tab" read -r name json value; do
  azd env set "AZURE_$name" "$value"
  azd env config set "infra.parameters.$(echo "$name" | tr '[:upper:]' '[:lower:]')" "$json"
done < "$work/outputs.env"
touch "$deployed_in_this_run"
