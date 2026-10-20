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

# The Carrier endpoints' credentials are derived from one random seed, made once. A new seed would change every
# credential the Partner portal has read from Key Vault, until it restarted.
if [ -z "${ESTATE_CARRIER_ENDPOINT_KEY_SEED:-}" ]; then
  ESTATE_CARRIER_ENDPOINT_KEY_SEED=$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')
  azd env set ESTATE_CARRIER_ENDPOINT_KEY_SEED "$ESTATE_CARRIER_ENDPOINT_KEY_SEED"
fi
export ESTATE_CARRIER_ENDPOINT_KEY_SEED

# Tearing the estate down soft-deletes its Key Vaults, and their names (derived from the resource group, so the same every
# time) stay taken until they are purged, which fails the next deployment with a ConflictError. A vault in deleted state
# holds nothing the estate still uses, so those of this environment's resource group are purged first. A vault with purge
# protection cannot be purged; it is reported and the deployment says so if it matters.
rg_id="/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/rg-$AZURE_ENV_NAME/"
az keyvault list-deleted --subscription "$AZURE_SUBSCRIPTION_ID" --resource-type vault \
  --query "[].[name, properties.location, properties.vaultId, properties.purgeProtectionEnabled]" --output tsv 2>/dev/null |
  while IFS="$(printf '\t')" read -r vault vault_location vault_id protected; do
    case "$vault_id" in
      "$rg_id"*) ;;
      *) continue ;;
    esac
    if [ "$protected" = "True" ] || [ "$protected" = "true" ]; then
      echo "Key Vault $vault is deleted with purge protection; it cannot be purged." >&2
      continue
    fi
    echo "Purging the deleted Key Vault $vault..."
    az keyvault purge --subscription "$AZURE_SUBSCRIPTION_ID" --name "$vault" --location "$vault_location" --output none ||
      echo "Could not purge $vault." >&2
  done

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

# Whether the TMS API, the Partner portal, the Driver app API and the ERP API take the Simulator's Reset estate (Reset:Enabled), which deletes every transactional
# record in them. Off unless the environment says so with ESTATE_RESET_ENABLED_SETTING=true. The Estate has 64 outputs, the most a template may have,
# so the setting is handed to the apps here and not as an output.
reset_enabled=$(printf '%s' "${ESTATE_RESET_ENABLED_SETTING:-false}" | tr '[:upper:]' '[:lower:]')
[ "$reset_enabled" = "true" ] || reset_enabled=false
azd env set AZURE_ESTATE_RESET_ENABLED "$reset_enabled"
azd env config set infra.parameters.estate_reset_enabled "\"$reset_enabled\""

# The seeded Drivers' Entra test users, whose domain and password the apps take as the Estate parameters azd asks for.
# ARM returns an output's name with its first letter lowercased (estatE_...), so it is looked up in upper case.
driver_app_registration_client_id=$(jq -r 'to_entries[] | select(.key | ascii_upcase == "ESTATE_DRIVER_APP_REGISTRATION_CLIENT_ID") | .value.value' "$work/outputs.json")
: "${driver_app_registration_client_id:?The estate has no ESTATE_DRIVER_APP_REGISTRATION_CLIENT_ID output}"
./create-driver-test-users.sh "$driver_app_registration_client_id"
touch "$deployed_in_this_run"
