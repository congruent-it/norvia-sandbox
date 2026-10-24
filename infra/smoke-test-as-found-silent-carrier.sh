#!/usr/bin/env bash
# Post-deploy presenter smoke run of the "silent Carrier" re-enactment (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set,
# signed in to the Azure CLI as the presenter the estate was provisioned for, once Shipment tracking works (P5):
#
#   infra/smoke-test-as-found-silent-carrier.sh <Transport Order number of a Shipment that is In transit>
#
# It checks that:
#   1. with the silent-carrier preset, the Carrier-tracking flow has no failed-run alert and its queue no dead-letter alert,
#      another flow still has both, and the TMS API has no Stale thresholds;
#   2. with the fault carrier-tracking-unmapped-code on (the presenter switches it in the Partner portal's presenter panel and
#      sends a Delivered event as the Carrier), the Carrier is told 202, the tracking queue's dead-letter queue holds the
#      message with UnmappableTrackingMessage, and the Shipment is still In transit;
#   3. with the fixed preset, the failed-run and dead-letter alerts exist for the flow and go to an action group with a receiver
#      (who is told), and the Stale thresholds are set.
# The presenter's observations (the Carrier's 202, the Shipper's screen, the Integration console's trail) are asked as yes or no.
# It ends in the fixed state.
set -euo pipefail

cd "$(dirname "$0")"
eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
number=${1:-}
queue=carrier-tracking

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $number =~ ^TO-[0-9]+$ ]] || fail "usage: $0 <Transport Order number>, such as TO-0000042"
[ -t 0 ] || fail "the Carrier's and the Shipper's sides are the presenter's, so run this in a terminal"
[[ -n ${ESTATE_ALERT_EMAIL:-} ]] || fail "set the receiver first: azd env set ESTATE_ALERT_EMAIL <address>"

fqdn() { az containerapp show --name "$1" --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv; }
portal="https://$(fqdn partner-portal-web)"
console="https://$(fqdn integration-console-web)"
cleanup() { ./as-found.sh fixed >/dev/null 2>&1 || echo "Put the estate back with infra/as-found.sh fixed" >&2; }
trap cleanup EXIT

rule() { az monitor metrics alert show --resource-group "$resource_group" --name "$1" "${@:2}" 2>/dev/null; }
tms_setting() {
  az containerapp show --name tms-api --resource-group "$resource_group" \
    --query "properties.template.containers[0].env[?name == '$1'].value | [0]" --output tsv
}
dead_letters() {
  az servicebus queue show --resource-group "$resource_group" --namespace-name "$AZURE_ESTATE_SERVICE_BUS_NAMESPACE" --name "$queue" \
    --query countDetails.deadLetterMessageCount --output tsv
}
confirm() {
  local answer
  read -r -p "$1 [y/N] " answer
  [[ $answer == y || $answer == Y ]] || fail "$2"
  pass "$3"
}

echo "1. The silent-carrier preset"
./as-found.sh silent-carrier
rule alert-failed-runs-carrier-tracking --output none && fail "alert-failed-runs-carrier-tracking exists" || pass "the Carrier-tracking flow has no failed-run alert"
rule alert-dead-letter-carrier-tracking --output none && fail "alert-dead-letter-carrier-tracking exists" || pass "the tracking queue has no dead-letter alert"
rule alert-failed-runs-carrier-booking --output none || fail "another flow lost its alert"
pass "another flow (carrier-booking) still has its failed-run alert"
[[ $(tms_setting ShipmentStale__ThresholdsSet) == false ]] || fail "the Stale thresholds are set under silent-carrier"
pass "the TMS API has no Stale thresholds"

echo
echo "2. The Carrier posts \"delivered\""
before=$(dead_letters)
cat <<MSG
  1. Open the presenter panel ($portal/presenter) and switch on carrier-tracking-unmapped-code for 5 minutes.
  2. As the Carrier of Transport Order $number, send its Delivered event in the Partner portal ($portal).
MSG
read -r -p "Press Enter when the Carrier has sent it. " _
confirm "Was the Carrier told 202 Accepted?" "the Carrier was not told 202" "the Carrier is told 202 Accepted"
for _ in $(seq 1 24); do
  [[ $(dead_letters) -gt $before ]] && break
  sleep 10
done
[[ $(dead_letters) -gt $before ]] || fail "the $queue dead-letter queue did not grow after 4 minutes"
pass "the $queue dead-letter queue holds the message"
confirm "Is the Shipper's screen for $number still on In transit?" "the Shipper's screen moved on" "the Shipper's screen stays on In transit"
echo "  3. Open the Integration console ($console/?key=$number), find the failed run and the dead-lettered message (UnmappableTrackingMessage)."
confirm "Did the console show the dead-lettered message and the fault?" "the console did not find the message" "the Integration console finds the dead-lettered message"
echo "  4. Switch the fault off in the presenter panel."
read -r -p "Press Enter when it is off. " _

echo
echo "3. The fixed run"
./as-found.sh fixed
for name in alert-failed-runs-carrier-tracking alert-dead-letter-carrier-tracking; do
  [[ $(rule "$name" --query enabled --output tsv) == true ]] || fail "$name is missing or disabled under the fixed preset"
  [[ $(rule "$name" --query 'length(actions)' --output tsv) -ge 1 ]] || fail "$name sends nowhere"
  pass "$name exists and sends to an action group"
done
receivers=$(az monitor action-group list --resource-group "$resource_group" --query "[?starts_with(name, 'ag-norvia-')] | [0].emailReceivers | length(@)" --output tsv)
[[ $receivers -ge 1 ]] || fail "the action group has no receiver"
pass "the action group has an email receiver: a run of the fault now tells someone"
[[ $(tms_setting ShipmentStale__ThresholdsSet) == true ]] || fail "the Stale thresholds are not set under the fixed preset"
pass "the TMS API has its Stale thresholds"
echo "Done: the estate is in the fixed state. Resubmit the dead-lettered message from the console to clear the dead-letter alert."
