#!/usr/bin/env bash
# Post-deploy presenter smoke run of the "contrast" re-enactment (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set, signed in
# to the Azure CLI as the presenter the estate was provisioned for, once Shipment tracking works (P5, P8, P12):
#
#   infra/smoke-test-as-found-contrast.sh <Transport Order number of a Shipment that is In transit>
#
# It checks that:
#   1. with the contrast preset (the healthy estate), the tracking flow's failed-run and dead-letter alerts exist;
#   2. with the fault tms-shipper-tracking-api-failing on, the Shipper's read fails at once and loudly (503, nothing queued), and the
#      fault tms-tracking-event-api-failing on, a Carrier's Tracking event is accepted with 202 and no message is dead-lettered;
#   3. once the faults have expired or been switched off, nothing is lost: the Carrier's event is recorded, the Shipment moved,
#      and the tracking dead-letter queue holds nothing new.
# The presenter's observations are asked as yes or no. It ends in the fixed state.
set -euo pipefail

cd "$(dirname "$0")"
eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
number=${1:-}
queue=carrier-tracking

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $number =~ ^TO-[0-9]+$ ]] || fail "usage: $0 <Transport Order number>, such as TO-0000042"
[ -t 0 ] || fail "the Shipper's and the Carrier's sides are the presenter's, so run this in a terminal"

fqdn() { az containerapp show --name "$1" --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv; }
portal="https://$(fqdn partner-portal-web)"
console="https://$(fqdn integration-console-web)"
cleanup() { ./as-found.sh fixed >/dev/null 2>&1 || echo "Put the estate back with infra/as-found.sh fixed" >&2; }
trap cleanup EXIT

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

echo "1. The contrast preset"
./as-found.sh contrast
for name in alert-failed-runs-carrier-tracking alert-dead-letter-carrier-tracking; do
  [[ $(az monitor metrics alert show --resource-group "$resource_group" --name "$name" --query enabled --output tsv) == true ]] \
    || fail "$name is missing or disabled"
  pass "$name exists"
done

echo
echo "2. The TMS fails for a few minutes"
before=$(dead_letters)
cat <<MSG
  1. Open the presenter panel ($portal/presenter) and switch on tms-shipper-tracking-api-failing and tms-tracking-event-api-failing
     for 2 minutes.
  2. As the Shipper of Transport Order $number, read its tracking in the Partner portal ($portal).
  3. As the Carrier, send the next Tracking event for it.
MSG
read -r -p "Press Enter when you have done both. " _
confirm "Did the Shipper see an error at once (503)?" "the Shipper's read did not fail loudly" "the failure is loud: the Shipper sees the error"
confirm "Was the Carrier told 202 Accepted?" "the Carrier was not told 202" "the Carrier's event is accepted and queued"
echo "  4. In the Integration console ($console/?key=$number) find the TMS hops: Failed, with the fault."
confirm "Did the console show the failed hops with the fault?" "the console did not show the fault" "the Integration console shows the failure and its cause"

echo
echo "3. The TMS is back and nothing is lost"
echo "  5. Wait for the faults to expire (or switch them off in the presenter panel), then wait for the event to be recorded."
read -r -p "Press Enter when the faults are off. " _
for _ in $(seq 1 24); do
  [[ $(dead_letters) -eq $before ]] || break
  sleep 5
done
[[ $(dead_letters) -eq $before ]] || fail "the $queue dead-letter queue grew: a message was lost to the dead-letter queue"
pass "nothing was dead-lettered"
confirm "Is the Carrier's event now on the Shipment's timeline, and the Shipment moved on?" "the event was lost" "nothing was lost: the event is recorded and the Shipment moved"
confirm "Does the Shipper's read work again?" "the Shipper's read still fails" "the Shipper's read works again"
echo "Done: the estate is in the fixed state."
