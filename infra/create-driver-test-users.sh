#!/bin/sh
# Makes an Entra test user for each active Driver of the seed file, and assigns it the app role of the Driver app's
# registration that carries the Driver ID (ADR 0037). Run by provision-estate.sh after the estate is deployed, with the
# Driver app's client ID. It is safe to run again: it finds the users it made and keeps the roles it assigned.
#
# A user's name is the Driver ID lowercased at the tenant's default domain, such as drv-0001@norvia.onmicrosoft.com. They
# share one password, made once and kept in the azd environment, which the presenter panel shows (the users hold no
# access to anything but the Driver app, and only through its roles). The domain and the password go to the apps as the
# Estate parameters azd asks for.
#
# It needs the rights to create users and assign app roles in the tenant, as the estate's Entra parts do.
set -eu
cd "$(dirname "$0")"

client_id=${1:?The client ID of the Driver app registration}
seed=../seed/norvia.seed.json

domain=$(az rest --method get --url https://graph.microsoft.com/v1.0/organization \
  --query "value[0].verifiedDomains[?isDefault].name | [0]" --output tsv)
[ -n "$domain" ] || { echo "The tenant has no default domain to make the Drivers' test users in." >&2; exit 1; }

# Made once, so the password the presenter panel shows stays the one the users have. The fixed parts satisfy Entra's complexity rules.
password=${ESTATE_DRIVER_SIGN_IN_PASSWORD:-}
if [ -z "$password" ]; then
  random=$(head -c 12 /dev/urandom | od -An -tx1 | tr -d ' \n')
  password="Nv-$random-Aa1"
  azd env set ESTATE_DRIVER_SIGN_IN_PASSWORD "$password"
fi

service_principal=$(az ad sp show --id "$client_id" --query id --output tsv)

jq -c '.drivers[] | select(.active)' "$seed" | while read -r driver; do
  id=$(echo "$driver" | jq -r .id)
  name=$(echo "$driver" | jq -r .name)
  user_name="$(echo "$id" | tr '[:upper:]' '[:lower:]')@$domain"

  if user_id=$(az ad user show --id "$user_name" --query id --output tsv 2>/dev/null); then
    az ad user update --id "$user_id" --password "$password" --force-change-password-next-sign-in false
  else
    user_id=$(az ad user create --display-name "$name" --user-principal-name "$user_name" --mail-nickname "$(echo "$id" | tr '[:upper:]' '[:lower:]')" \
      --password "$password" --force-change-password-next-sign-in false --query id --output tsv)
  fi

  role_id=$(az ad app show --id "$client_id" --query "appRoles[?value=='$id'].id | [0]" --output tsv)
  [ -n "$role_id" ] || { echo "The Driver app has no app role $id. Deploy the estate again." >&2; exit 1; }

  assigned=$(az rest --method get --url "https://graph.microsoft.com/v1.0/users/$user_id/appRoleAssignments" \
    --query "value[?resourceId=='$service_principal' && appRoleId=='$role_id'] | length(@)" --output tsv)
  if [ "$assigned" = "0" ]; then
    az rest --method post --url "https://graph.microsoft.com/v1.0/users/$user_id/appRoleAssignments" \
      --headers "Content-Type=application/json" \
      --body "{\"principalId\":\"$user_id\",\"resourceId\":\"$service_principal\",\"appRoleId\":\"$role_id\"}" --output none
  fi
  echo "Driver $id signs in as $user_name"
done

# The Estate parameters, as provision-estate.sh sets the estate's outputs.
set_parameter() {
  azd env set "AZURE_$1" "$2"
  azd env config set "infra.parameters.$(echo "$1" | tr '[:upper:]' '[:lower:]')" "$(printf '%s' "$2" | jq -R .)"
}
set_parameter ESTATE_DRIVER_SIGN_IN_DOMAIN "$domain"
set_parameter ESTATE_DRIVER_SIGN_IN_PASSWORD "$password"
