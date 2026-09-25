#!/bin/sh
# azd postdeploy hook: lets Entra send the presenter back to the demo UIs, the TMS UI and the Partner portal, after they
# sign in at the gate in front of them (ADR 0024). The estate can't do this itself: it is deployed before the apps,
# whose addresses it doesn't know yet.
set -eu

resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
client_id=${AZURE_ESTATE_DEMO_UIS_CLIENT_ID:?Deploy the estate first: azd hooks run preprovision}

hosts=""
redirect_uris=""
for app in tms-web partner-portal-web; do
  host=$(az containerapp show --subscription "$AZURE_SUBSCRIPTION_ID" --name "$app" --resource-group "$resource_group" \
    --query properties.configuration.ingress.fqdn --output tsv)
  hosts="${hosts:+$hosts,}$host"
  redirect_uris="$redirect_uris https://$host/.auth/login/aad/callback"
  echo "The presenter opens $app at https://$host"
done

# Kept in the azd environment too, so deploying the estate again keeps the redirect URIs.
azd env set ESTATE_DEMO_UI_HOSTS "$hosts"
# shellcheck disable=SC2086 # one argument per redirect URI
az ad app update --id "$client_id" --web-redirect-uris $redirect_uris
