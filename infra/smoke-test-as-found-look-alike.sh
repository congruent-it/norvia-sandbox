#!/usr/bin/env bash
# Post-deploy presenter smoke run of the "look-alike" re-enactment (not in CI), after `azd up`, signed in to the Azure CLI as the
# presenter the estate was provisioned for, once Shipment tracking works (P5, P8):
#
#   infra/smoke-test-as-found-look-alike.sh <Transport Order number of a Shipment that is In transit>
#
# It checks that:
#   1. with the look-alike preset, the Shipper tracking policy of API Management holds the cache (none under fixed), and no
#      alert, fault or Stale threshold is changed: the estate is otherwise healthy;
#   2. the TMS is right and the Shipper sees an old status: after the Shipper has read the Transport Order and the Carrier has
#      moved the Shipment on, the Partner portal's Shipper tracking screen still shows the old status;
#   3. with the fixed preset, the same read shows the Shipment's true status.
# The presenter's observations are asked as yes or no. It ends in the fixed state.
set -euo pipefail

cd "$(dirname "$0")"
eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
number=${1:-}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $number =~ ^TO-[0-9]+$ ]] || fail "usage: $0 <Transport Order number>, such as TO-0000042"
[ -t 0 ] || fail "the Shipper's and the Carrier's sides are the presenter's, so run this in a terminal"

portal="https://$(az containerapp show --name partner-portal-web --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
api_management=${AZURE_ESTATE_API_MANAGEMENT:?Deploy the estate first: azd hooks run preprovision}
cleanup() { ./as-found.sh fixed >/dev/null 2>&1 || echo "Put the estate back with infra/as-found.sh fixed" >&2; }
trap cleanup EXIT

tracking_policy() {
  az rest --method get --output tsv --query properties.value \
    --url "https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers/Microsoft.ApiManagement/service/$api_management/apis/norvia-partner-api/operations/get-shipper-transport-order/policies/policy?api-version=2024-05-01&format=rawxml"
}
confirm() {
  local answer
  read -r -p "$1 [y/N] " answer
  [[ $answer == y || $answer == Y ]] || fail "$2"
  pass "$3"
}

echo "1. The look-alike preset"
./as-found.sh look-alike
tracking_policy | grep -q "cache-lookup-value" || fail "the Shipper tracking policy has no cache under look-alike"
pass "the Shipper tracking policy caches the answer"
[[ $(az monitor metrics alert show --resource-group "$resource_group" --name alert-failed-runs-carrier-tracking --query enabled --output tsv) == true ]] \
  || fail "the alerts are not intact under look-alike"
pass "the alerts are intact: nothing else is broken"

echo
echo "2. The TMS is right and the Shipper sees an old status"
cat <<MSG
  1. As the Shipper of Transport Order $number, open its tracking in the Partner portal ($portal) and note the status.
  2. As the Carrier, send the next Tracking event (it moves the Shipment on). Check the Shipment in the TMS: its status moved.
  3. As the Shipper, read the Transport Order again.
MSG
read -r -p "Press Enter when you have read it again. " _
confirm "Did the TMS show the new status?" "the TMS did not move on, so the scenario has nothing to show" "the TMS has the new status"
confirm "Does the Shipper still see the old status?" "the Shipper saw the new status: the cache is not in the way" "the Shipper sees the old status (the cache, not the TMS)"

echo
echo "3. The fixed run"
./as-found.sh fixed
tracking_policy | grep -q "cache-lookup-value" && fail "the Shipper tracking policy caches under the fixed preset"
pass "the Shipper tracking policy has no cache"
echo "  4. As the Shipper, read the Transport Order again."
read -r -p "Press Enter when you have read it. " _
confirm "Does the Shipper see the Shipment's true status?" "the Shipper still sees the old status" "the Shipper sees the true status"
echo "Done: the estate is in the fixed state."
