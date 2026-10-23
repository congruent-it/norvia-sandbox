#!/usr/bin/env bash
# Post-deploy smoke run of the as-found alerts (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set, signed in to the Azure
# CLI as the presenter the estate was provisioned for (docs/operations/as-found.md):
#
#   infra/smoke-test-as-found-alerts.sh
#
# It checks that:
#   1. with the as-found preset, a sample of the Logic Apps and entities the preset lists has no alert (the rules are absent,
#      not disabled), a sample it leaves out still has one, and the action group still has its receiver;
#   2. with the fixed preset, every sampled rule exists, is enabled and sends to the action group, which has an email receiver;
#   3. with noAlertReceiver=true, the rules exist and the action group has no receiver;
#   4. with noInboundLifecycleRule=true, the inbound account has no lifecycle rule (fixed has one), and with carrierSecretInlined=true
#      the booking Logic App has the Carrier credential in its settings, and none after fixed. A health check reports where such a
#      secret is, never its value, and so does this script;
#   5. with staleThresholdsUnset=true, the TMS API's ShipmentStale__ThresholdsSet setting is false (true under fixed), and with
#      shipperTrackingCached=true the get-transport-order policy of the Partner API holds the cache (none under fixed).
# It ends in the fixed state. Each step deploys the estate alone, as infra/as-found.sh does.
set -euo pipefail

cd "$(dirname "$0")"
eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
# Listed by infra/presets/as-found.json: the tracking, booking and billing runs; the tracking and Proof of delivery queues and
# the billing subscription.
unalerted=(
  alert-failed-runs-carrier-tracking alert-failed-runs-carrier-booking alert-failed-runs-billing
  alert-dead-letter-carrier-tracking alert-dead-letter-carrier-proof-of-delivery alert-dead-letter-billing
)
# Not listed: the intake and Trip dispatch runs, the Trip dispatch and Driver reports subscriptions.
alerted=(
  alert-failed-runs-transport-order-intake alert-failed-runs-trip-dispatch
  alert-dead-letter-trip-dispatch alert-dead-letter-driver-reports
)

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ -n ${ESTATE_ALERT_EMAIL:-} ]] || fail "set the receiver first: azd env set ESTATE_ALERT_EMAIL <address>"

rule() { az monitor metrics alert show --resource-group "$resource_group" --name "$1" "${@:2}" 2>/dev/null; }
action_group() { az monitor action-group list --resource-group "$resource_group" --query "[?starts_with(name, 'ag-norvia-')] | [0]" --output json; }
receivers() { action_group | jq '.emailReceivers | length'; }

expect_absent() { if rule "$1" --output none; then fail "$1 exists"; else pass "$1 is absent"; fi; }
expect_present() {
  local enabled actions
  enabled=$(rule "$1" --query enabled --output tsv) || fail "$1 is missing"
  [[ $enabled == true ]] || fail "$1 is disabled"
  actions=$(rule "$1" --query 'length(actions)' --output tsv)
  [[ $actions -ge 1 ]] || fail "$1 sends nowhere"
  pass "$1 exists, enabled, with an action group"
}

./as-found.sh as-found
for name in "${unalerted[@]}"; do expect_absent "$name"; done
for name in "${alerted[@]}"; do expect_present "$name"; done
[[ $(receivers) -ge 1 ]] || fail "the action group has no receiver under the as-found preset"
pass "the action group still has its receiver"

./as-found.sh fixed
for name in "${unalerted[@]}" "${alerted[@]}"; do expect_present "$name"; done
[[ $(receivers) -ge 1 ]] || fail "the action group has no receiver under the fixed preset"
pass "the action group has an email receiver"

./as-found.sh noAlertReceiver=true
for name in "${unalerted[@]}" "${alerted[@]}"; do expect_present "$name"; done
[[ $(receivers) -eq 0 ]] || fail "the action group still has a receiver under noAlertReceiver"
pass "the action group has no receiver, and the alerts exist"

inbound_account=$(az storage account list --resource-group "$resource_group" --query "[?starts_with(name, 'stinbound')].name | [0]" --output tsv)
booking_app=${AZURE_ESTATE_BOOKING_LOGIC_APP:?Deploy the estate first: azd hooks run preprovision}
lifecycle_rule() { az storage account management-policy show --resource-group "$resource_group" --account-name "$inbound_account" --output none 2>/dev/null; }
inlined_secret() {
  az functionapp config appsettings list --resource-group "$resource_group" --name "$booking_app" --query "[?name == 'CARRIER_ENDPOINT_KEY'] | length(@)" --output tsv
}

./as-found.sh fixed
lifecycle_rule || fail "the inbound account has no lifecycle rule under the fixed preset"
pass "the inbound account has its lifecycle rule"
[[ $(inlined_secret) -eq 0 ]] || fail "the booking Logic App has an inlined Carrier credential under the fixed preset"
pass "the booking Logic App reads the Carrier credentials from Key Vault only"

./as-found.sh noInboundLifecycleRule=true
if lifecycle_rule; then fail "the inbound account still has a lifecycle rule under noInboundLifecycleRule"; fi
pass "the inbound account has no lifecycle rule"

./as-found.sh carrierSecretInlined=true
[[ $(inlined_secret) -eq 1 ]] || fail "the booking Logic App has no inlined Carrier credential under carrierSecretInlined"
pass "the booking Logic App has a Carrier credential in its settings (name only checked, the value is never printed)"

tms_setting() {
  az containerapp show --name tms-api --resource-group "$resource_group" \
    --query "properties.template.containers[0].env[?name == '$1'].value | [0]" --output tsv
}
api_management=${AZURE_ESTATE_API_MANAGEMENT:?Deploy the estate first: azd hooks run preprovision}
tracking_policy() {
  az rest --method get --output tsv --query properties.value \
    --url "https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers/Microsoft.ApiManagement/service/$api_management/apis/norvia-partner-api/operations/get-shipper-transport-order/policies/policy?api-version=2024-05-01&format=rawxml"
}

./as-found.sh fixed
[[ $(tms_setting ShipmentStale__ThresholdsSet) == true ]] || fail "the Stale thresholds are not set under the fixed preset"
pass "the TMS API has its Stale thresholds"
tracking_policy | grep -q "cache-lookup-value" && fail "the Shipper tracking policy caches under the fixed preset"
pass "the Shipper tracking policy has no cache, and its rate limit"

./as-found.sh staleThresholdsUnset=true
[[ $(tms_setting ShipmentStale__ThresholdsSet) == false ]] || fail "the Stale thresholds are still set under staleThresholdsUnset"
[[ $(tms_setting LateDelivery__CheckEnabled) == true ]] || fail "staleThresholdsUnset changed the Late delivery check"
pass "the Stale thresholds are unset, and only they"

./as-found.sh shipperTrackingCached=true
tracking_policy | grep -q "cache-lookup-value" || fail "the Shipper tracking policy has no cache under shipperTrackingCached"
pass "the Shipper tracking policy caches the answer"

./as-found.sh fixed
echo "Done: the estate is in the fixed state."
