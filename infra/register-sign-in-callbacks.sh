#!/bin/sh
# azd postdeploy hook: lets Entra send a user back to the app they signed in to at its Easy Auth: the presenter to the
# demo UIs (ADR 0024), an Integration operator to the Integration console (ADR 0026). The estate can't do this itself:
# it is deployed before the apps, whose addresses it doesn't know yet. The container apps whose Easy Auth signs in to an
# app registration are the ones the AppHost put behind it.
set -eu

resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}

# Every container app with Easy Auth, as "<app> <client ID it signs in to> <host name>".
signed_in_apps=$(
  for app in $(az containerapp list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" \
    --query "[].name" --output tsv); do
    client_id=$(az containerapp auth show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$app" --resource-group "$resource_group" \
      --query identityProviders.azureActiveDirectory.registration.clientId --output tsv 2>/dev/null || true)
    [ -n "$client_id" ] || continue
    host=$(az containerapp show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$app" --resource-group "$resource_group" \
      --query properties.configuration.ingress.fqdn --output tsv)
    echo "$app $client_id $host"
  done
)

# register <app registration's client ID> <what it is> <azd environment variable that keeps its callbacks>
register() {
  client_id=$1
  what=$2
  kept_as=$3

  redirect_uris=""
  while read -r app gate host; do
    [ "$gate" = "$client_id" ] || continue
    # Easy Auth's callback for Entra.
    redirect_uris="${redirect_uris:+$redirect_uris,}https://$host/.auth/login/aad/callback"
    echo "Open $app at https://$host"
  done <<APPS
$signed_in_apps
APPS
  [ -n "$redirect_uris" ] || { echo "No container app signs in to $what's app registration $client_id." >&2; exit 1; }

  # Kept in the azd environment too, so deploying the estate again keeps the redirect URIs.
  azd env set "$kept_as" "$redirect_uris"
  # shellcheck disable=SC2046 # one argument per redirect URI
  az ad app update --id "$client_id" --web-redirect-uris $(echo "$redirect_uris" | tr ',' ' ')
}

register "${AZURE_ESTATE_DEMO_UIS_CLIENT_ID:?Deploy the estate first: azd hooks run preprovision}" \
  "the demo UIs" ESTATE_DEMO_UI_REDIRECT_URIS
register "${AZURE_ESTATE_INTEGRATION_CONSOLE_CLIENT_ID:?Deploy the estate first: azd hooks run preprovision}" \
  "the Integration console" ESTATE_INTEGRATION_CONSOLE_REDIRECT_URIS
