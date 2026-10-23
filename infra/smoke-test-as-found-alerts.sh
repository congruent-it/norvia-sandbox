#!/usr/bin/env bash
# Post-deploy smoke run of the as-found alerts (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set, signed in to the Azure
# CLI as the presenter the estate was provisioned for (docs/operations/as-found.md):
#
#   infra/smoke-test-as-found-alerts.sh
#
# It checks that:
#   1. with the as-found preset, the Carrier-tracking Logic App has no failed-run alert and the tracking queue has no
#      dead-letter alert (the rules are absent, not disabled), and the action group still has its receiver;
#   2. with the fixed preset, both rules exist, are enabled and send to the action group, which has an email receiver;
#   3. with noAlertReceiver=true, the rules exist and the action group has no receiver.
# It ends in the fixed state. Each step deploys the estate alone, as infra/as-found.sh does.
set -euo pipefail

cd "$(dirname "$0")"
eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
failed_runs=alert-failed-runs-carrier-tracking
dead_letter=alert-dead-letter-carrier-tracking

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
expect_absent "$failed_runs"
expect_absent "$dead_letter"
[[ $(receivers) -ge 1 ]] || fail "the action group has no receiver under the as-found preset"
pass "the action group still has its receiver"

./as-found.sh fixed
expect_present "$failed_runs"
expect_present "$dead_letter"
[[ $(receivers) -ge 1 ]] || fail "the action group has no receiver under the fixed preset"
pass "the action group has an email receiver"

./as-found.sh noAlertReceiver=true
expect_present "$failed_runs"
expect_present "$dead_letter"
[[ $(receivers) -eq 0 ]] || fail "the action group still has a receiver under noAlertReceiver"
pass "the action group has no receiver, and the alerts exist"

./as-found.sh fixed
echo "Done: the estate is in the fixed state."
