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

# Defender for Storage's malware scanning subscribes to the storage account's blob events through Event Grid. In a subscription that never
# registered Microsoft.EventGrid that subscription fails ("Microsoft.EventGrid is not registered"), and the account is left with
# on-upload scanning off although its deployment succeeded. Registering is idempotent, and --wait holds until it is done.
if [ "$(az provider show --subscription "$AZURE_SUBSCRIPTION_ID" --namespace Microsoft.EventGrid --query registrationState --output tsv 2>/dev/null || true)" != Registered ]; then
  echo "Registering Microsoft.EventGrid, which malware scanning needs..."
  az provider register --subscription "$AZURE_SUBSCRIPTION_ID" --namespace Microsoft.EventGrid --wait
fi

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

# The same goes for API Management services and Foundry (Cognitive Services) accounts: their names (also derived from the
# resource group) stay taken while they are soft-deleted, which fails the deployment with ServiceAlreadyExistsInSoftDeletedState.
# Those of this environment's resource group are purged first.
az apim deletedservice list --subscription "$AZURE_SUBSCRIPTION_ID" \
  --query "[].[name, location, serviceId]" --output tsv 2>/dev/null |
  while IFS="$(printf '\t')" read -r apim apim_location apim_id; do
    case "$apim_id" in
      "$rg_id"*|"${rg_id%/}"/*) ;;
      *) continue ;;
    esac
    echo "Purging the deleted API Management service $apim..."
    az apim deletedservice purge --subscription "$AZURE_SUBSCRIPTION_ID" --service-name "$apim" --location "$apim_location" --output none ||
      echo "Could not purge $apim." >&2
  done

az cognitiveservices account list-deleted --subscription "$AZURE_SUBSCRIPTION_ID" \
  --query "[].[name, location, id]" --output tsv 2>/dev/null |
  while IFS="$(printf '\t')" read -r account account_location account_id; do
    case "$account_id" in
      *"/resourceGroups/rg-$AZURE_ENV_NAME/"*|*"/resourcegroups/rg-$AZURE_ENV_NAME/"*) ;;
      *) continue ;;
    esac
    echo "Purging the deleted Foundry account $account..."
    az cognitiveservices account purge --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" \
      --name "$account" --location "$account_location" --output none ||
      echo "Could not purge $account." >&2
  done

# Aspire's Azure SQL roles are granted by deployment scripts, each of which starts a container group. A script that failed (the region's
# container group quota, say) stays behind, and the next `azd provision` fails on it with "resource already exists ... pending delete".
# Its work is only SQL role grants, which run again, so the failed scripts of this resource group are deleted first.
failed_scripts=$(az rest --method get --output tsv 2>/dev/null \
  --url "https://management.azure.com${rg_id}providers/Microsoft.Resources/deploymentScripts?api-version=2023-08-01" \
  --query "value[?properties.provisioningState=='Failed'].id" || true)
for script in $failed_scripts; do
  echo "Deleting the failed deployment script ${script##*/}..."
  az rest --method delete --url "https://management.azure.com${script}?api-version=2023-08-01" --output none ||
    echo "Could not delete ${script##*/}." >&2
  # The delete is asynchronous, and a script still being deleted fails the next deployment the same way, so wait until it is gone.
  for _ in 1 2 3 4 5 6 7 8 9 10 11 12; do
    az rest --method get --url "https://management.azure.com${script}?api-version=2023-08-01" --output none 2>/dev/null || break
    sleep 10
  done
done
# Even once it can no longer be read, ARM's identity service can keep the script as "pending delete" for a while longer, and a deployment
# that creates it again then fails with "Conflict ... pending delete". So after a delete, wait a little more before deploying.
[ -z "$failed_scripts" ] || { echo "Waiting for the deleted deployment scripts to clear..."; sleep 120; }

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

# A new subscription starts with a $Default rule that passes every message, and the Bicep adds each subscription's real filter as
# the rule "resubmit-aware". (A rule named $Default with a SQL filter in the Bicep does not work: Service Bus accepts it, then the
# rule is gone and the subscription receives nothing.) Deploying again never deletes a resource, so the $Default rules are
# deleted here, or each subscription would keep passing everything. Deleting one that is already gone is harmless.
# ARM changes the case of an output's name ("estatE_SERVICE_BUS_NAMESPACE"), so it is matched upper-cased, as further down.
service_bus=$(jq -r 'to_entries[] | select(.key | ascii_upcase == "ESTATE_SERVICE_BUS_NAMESPACE") | .value.value' "$work/outputs.json")
[ -n "$service_bus" ] || { echo "The estate has no Service Bus namespace output." >&2; exit 1; }
az servicebus topic list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" --namespace-name "$service_bus" \
  --query "[].name" --output tsv | tr -d '\r' |
  while read -r topic; do
    [ -n "$topic" ] || continue
    az servicebus topic subscription list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" \
      --namespace-name "$service_bus" --topic-name "$topic" --query "[].name" --output tsv | tr -d '\r' |
      while read -r subscription; do
        [ -n "$subscription" ] || continue
        if az servicebus topic subscription rule show --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" \
          --namespace-name "$service_bus" --topic-name "$topic" --subscription-name "$subscription" --name '$Default' --output none 2>/dev/null; then
          echo "Deleting the \$Default rule of $topic/$subscription..."
          az servicebus topic subscription rule delete --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" \
            --namespace-name "$service_bus" --topic-name "$topic" --subscription-name "$subscription" --name '$Default' --output none
        fi
      done
  done

# Deploying again never deletes a resource, so an alert the as-found toggles now leave out is deleted here: a "no alert" is
# an absent rule, never a disabled one. The names are those infra/estate/alerts.bicep gives its rules.
if [ -n "${ESTATE_ASFOUND:-}" ]; then
  {
    printf '%s' "$ESTATE_ASFOUND" | jq -r '(.flowsWithoutFailedRunAlert // [])[] | "alert-failed-runs-" + .'
    printf '%s' "$ESTATE_ASFOUND" | jq -r '(.entitiesWithoutDeadLetterAlert // [])[] | "alert-dead-letter-" + .'
  } | while read -r rule; do
    if az monitor metrics alert show --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" --name "$rule" --output none 2>/dev/null; then
      echo "Deleting the alert $rule (as-found)..."
      az monitor metrics alert delete --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" --name "$rule"
    fi
  done

  # The same for the storage settings the toggles leave out. The estate's accounts are named stinbound<token> and stdriver<token>; the
  # TMS's is Aspire's (tagged aspire-resource-name=storage), and may not exist yet on the first deployment.
  toggle() { printf '%s' "$ESTATE_ASFOUND" | jq -r --arg name "$1" '.[$name] // false'; }
  account() { az storage account list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" --query "$1" --output tsv; }
  delete_lifecycle_rule() {
    [ -n "$1" ] || return 0
    if az storage account management-policy show --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" --account-name "$1" --output none 2>/dev/null; then
      echo "Deleting the lifecycle rule of $1 (as-found)..."
      az storage account management-policy delete --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" --account-name "$1"
    fi
  }
  [ "$(toggle noInboundLifecycleRule)" != true ] || delete_lifecycle_rule "$(account "[?starts_with(name, 'stinbound')].name | [0]")"
  [ "$(toggle noTransitLifecycleRule)" != true ] || delete_lifecycle_rule "$(account "[?starts_with(name, 'stdriver')].name | [0]")"
  [ "$(toggle proofFilesStayHot)" != true ] || delete_lifecycle_rule "$(account "[?tags.\"aspire-resource-name\" == 'storage'].name | [0]")"
  if [ "$(toggle noMalwareScanning)" = true ]; then
    inbound_id=$(account "[?starts_with(name, 'stinbound')].id | [0]")
    if [ -n "$inbound_id" ]; then
      # The setting cannot be deleted ("DeleteNotSupported"), so "absent" is the setting turned off, for this account whatever the
      # subscription's own Defender plan says. It is only written when scanning is on, and the estate Bicep turns it on again when the
      # toggle is cleared.
      defender_url="https://management.azure.com$inbound_id/providers/Microsoft.Security/defenderForStorageSettings/current?api-version=2022-12-01-preview"
      scanning=$(az rest --method get --url "$defender_url" --query "properties.malwareScanning.onUpload.isEnabled || properties.isEnabled" --output tsv 2>/dev/null || true)
      if [ "$scanning" = true ] || [ "$scanning" = True ]; then
        echo "Turning off Defender for Storage malware scanning on the inbound account (as-found)..."
        az rest --method put --url "$defender_url" --output none \
          --body '{"properties":{"isEnabled":false,"overrideSubscriptionLevelSettings":true,"malwareScanning":{"onUpload":{"isEnabled":false}},"sensitiveDataDiscovery":{"isEnabled":false}}}' ||
          echo "Could not turn off malware scanning." >&2
      fi
    fi
  fi
fi

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

# Whether the Agents do anything (Agents:Enabled), and the Foundry project endpoint the apps and scripts call. Off unless the environment says so
# with ESTATE_AGENTS_ENABLED_SETTING=true. Like the reset setting, both are handed to the apps here, because the Estate is at its 64 outputs.
# The account is the estate's only AIServices account; the projects are norvia and norvia-intake, the Intake agent's (infra/estate/foundry.bicep).
agents_enabled=$(printf '%s' "${ESTATE_AGENTS_ENABLED_SETTING:-false}" | tr '[:upper:]' '[:lower:]')
[ "$agents_enabled" = "true" ] || agents_enabled=false
foundry_account=$(az cognitiveservices account list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" \
  --query "[?kind=='AIServices'].name | [0]" --output tsv)
: "${foundry_account:?The estate has no Foundry account}"
foundry_project_endpoint="https://$foundry_account.services.ai.azure.com/api/projects/norvia"
foundry_intake_project_endpoint="https://$foundry_account.services.ai.azure.com/api/projects/norvia-intake"
azd env set AZURE_ESTATE_AGENTS_ENABLED "$agents_enabled"
azd env config set infra.parameters.estate_agents_enabled "\"$agents_enabled\""
azd env set AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT "$foundry_project_endpoint"
azd env config set infra.parameters.estate_foundry_project_endpoint "\"$foundry_project_endpoint\""
azd env set FOUNDRY_PROJECT_ENDPOINT "$foundry_project_endpoint"
azd env set FOUNDRY_INTAKE_PROJECT_ENDPOINT "$foundry_intake_project_endpoint"

# The Integration console's agent API: the app registration API Management's identity calls it as, and that identity. The estate is at its 64
# outputs, so they are found by name (infra/estate/integration-operations-mcp.bicep).
console_agent_api_client_id=$(az ad app list --filter "displayName eq 'Norvia Integration console agent API ($AZURE_ENV_NAME)'" --query '[0].appId' --output tsv)
: "${console_agent_api_client_id:?The estate has no Integration console agent API app registration}"
apim_client_id=$(az identity list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" \
  --query "[?starts_with(name, 'id-apim-')].clientId | [0]" --output tsv)
: "${apim_client_id:?The estate has no API Management identity}"
azd env set AZURE_ESTATE_INTEGRATION_CONSOLE_AGENT_API_CLIENT_ID "$console_agent_api_client_id"
azd env config set infra.parameters.estate_integration_console_agent_api_client_id "\"$console_agent_api_client_id\""
azd env set AZURE_ESTATE_API_MANAGEMENT_CLIENT_ID "$apim_client_id"
azd env config set infra.parameters.estate_api_management_client_id "\"$apim_client_id\""

# The Estate health check: its app registration (the one Easy Auth signs operators in to), its agent API's app registration (the audience of the
# tokens API Management sends it) and its identity (Reader on the resource group). The estate is at its 64 outputs, so they are found by name
# (infra/estate/health-check.bicep, infra/estate/health-check-mcp.bicep).
health_check_client_id=$(az ad app list --filter "displayName eq 'Norvia Estate health check ($AZURE_ENV_NAME)'" --query '[0].appId' --output tsv)
: "${health_check_client_id:?The estate has no Estate health check app registration}"
health_check_agent_api_client_id=$(az ad app list --filter "displayName eq 'Norvia Estate health check agent API ($AZURE_ENV_NAME)'" --query '[0].appId' --output tsv)
: "${health_check_agent_api_client_id:?The estate has no Estate health check agent API app registration}"
health_check_identity_name=$(az identity list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "rg-$AZURE_ENV_NAME" \
  --query "[?starts_with(name, 'id-health-check-')].name | [0]" --output tsv)
: "${health_check_identity_name:?The estate has no Estate health check identity}"
azd env set AZURE_ESTATE_HEALTH_CHECK_CLIENT_ID "$health_check_client_id"
azd env config set infra.parameters.estate_health_check_client_id "\"$health_check_client_id\""
azd env set AZURE_ESTATE_HEALTH_CHECK_AGENT_API_CLIENT_ID "$health_check_agent_api_client_id"
azd env config set infra.parameters.estate_health_check_agent_api_client_id "\"$health_check_agent_api_client_id\""
azd env set AZURE_ESTATE_HEALTH_CHECK_IDENTITY_NAME "$health_check_identity_name"
azd env config set infra.parameters.estate_health_check_identity_name "\"$health_check_identity_name\""

# The seeded Drivers' Entra test users, whose domain and password the apps take as the Estate parameters azd asks for.
# ARM returns an output's name with its first letter lowercased (estatE_...), so it is looked up in upper case.
driver_app_registration_client_id=$(jq -r 'to_entries[] | select(.key | ascii_upcase == "ESTATE_DRIVER_APP_REGISTRATION_CLIENT_ID") | .value.value' "$work/outputs.json")
: "${driver_app_registration_client_id:?The estate has no ESTATE_DRIVER_APP_REGISTRATION_CLIENT_ID output}"
./create-driver-test-users.sh "$driver_app_registration_client_id"
touch "$deployed_in_this_run"
