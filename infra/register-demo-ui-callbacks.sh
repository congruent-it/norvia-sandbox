#!/bin/sh
# azd postdeploy hook: lets Entra send the presenter back to the demo UIs after they sign in at the gate in front of
# them (ADR 0024). The estate can't do this itself: it is deployed before the apps, whose addresses it doesn't know yet.
# The demo UIs are the container apps whose gate signs in to the estate's demo UIs app registration; the AppHost says
# which ones those are.
set -eu

resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
client_id=${AZURE_ESTATE_DEMO_UIS_CLIENT_ID:?Deploy the estate first: azd hooks run preprovision}

redirect_uris=""
for app in $(az containerapp list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" \
  --query "[].name" --output tsv); do
  gate=$(az containerapp auth show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$app" --resource-group "$resource_group" \
    --query identityProviders.azureActiveDirectory.registration.clientId --output tsv 2>/dev/null || true)
  [ "$gate" = "$client_id" ] || continue
  host=$(az containerapp show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$app" --resource-group "$resource_group" \
    --query properties.configuration.ingress.fqdn --output tsv)
  # Easy Auth's callback for Entra.
  redirect_uris="${redirect_uris:+$redirect_uris,}https://$host/.auth/login/aad/callback"
  echo "The presenter opens $app at https://$host"
done
[ -n "$redirect_uris" ] || { echo "No container app signs in to the demo UIs' app registration $client_id." >&2; exit 1; }

# Kept in the azd environment too, so deploying the estate again keeps the redirect URIs.
azd env set ESTATE_DEMO_UI_REDIRECT_URIS "$redirect_uris"
# shellcheck disable=SC2046 # one argument per redirect URI
az ad app update --id "$client_id" --web-redirect-uris $(echo "$redirect_uris" | tr ',' ' ')
