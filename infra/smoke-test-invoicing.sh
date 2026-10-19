#!/usr/bin/env bash
# Post-deploy presenter smoke run of Invoicing, the fault injection and the tracing (E1.11, not in CI). The happy path (Invoice
# now, Credit note, the Shipper's inbox) is played by the Simulator (docs/operations/simulator.md); this run keeps what it
# doesn't: the two faults and finding the path in Application Insights. The faults are injected the way the other smoke runs
# do it, by pointing a Logic App at an address that doesn't answer and putting it back, because Logic Apps Standard has no
# way to stop one hop. The presenter's actions (making an order Billable, Invoice now, Send again, in the TMS UI and the ERP
# UI) are theirs: the script stops and says what to do. Run it in a terminal, signed in to the Azure CLI as the presenter the
# estate was provisioned for, once the Billing and Shipper invoice Logic Apps are deployed (azd up does it):
#
#   infra/smoke-test-invoicing.sh <Transport Order number for the ERP fault> <Transport Order number for the Shipper fault>
#
# Get two Transport Orders Delivered with a Proof of delivery, so that each becomes Billable the moment the presenter says so,
# for example by releasing them in the TMS UI. Use two Shippers, or invoice the first before the second is Billable: the
# second invoice must hold the second order's line. It checks that:
#   1. with the ERP billing API unreachable for the Billing Logic App, the first order's TransportOrderBillable is retried and
#      then dead-lettered on the Billing subscription, with ErpUnavailable as the reason, and the order's line is not among the
#      ERP's Waiting lines (the presenter looks in the ERP UI and confirms);
#   2. with the Shipper invoice endpoint unreachable for the Shipper invoice Logic App, the second order's invoice, issued by
#      the presenter's Invoice now, ends Not delivered: the Logic App's send is tracked Failed with ShipperUnavailable and the
#      ERP recorded the report;
#   3. after the endpoint is back and the presenter's Send again, the invoice is Sent: the Logic App's send is tracked
#      Succeeded and the ERP recorded the Sent report;
#   4. the path is findable in Application Insights by correlation ID: the dead-lettered message in the Billing Logic App and
#      the TMS, and the invoice in the ERP, the Shipper invoice Logic App and the Partner portal.
# It prints identifiers only: never a body, a token or a key. What only a Persona's or Finance's page shows (the Waiting lines,
# the Not delivered flag, the Shipper's inbox) the script lists at the end for the presenter to open. The presenter's steps are
# in docs/processes/erp/e1-invoicing.md, "Presenter smoke run".
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
erp_fault_order=${1:-}
shipper_fault_order=${2:-}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $erp_fault_order =~ ^TO-[0-9]+$ && $shipper_fault_order =~ ^TO-[0-9]+$ && $erp_fault_order != "$shipper_fault_order" ]] \
  || fail "usage: $0 <Transport Order number for the ERP fault> <Transport Order number for the Shipper fault>, two different ones such as TO-0000042 TO-0000043"
[ -t 0 ] || fail "the presenter's actions are the presenter's, so run this in a terminal"

fqdn() { az containerapp show --name "$1" --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv; }
tms_ui="https://$(fqdn tms-web)"
erp_ui="https://$(fqdn erp-web)"
console="https://$(fqdn integration-console-web)"
billing_logic_app=$AZURE_ESTATE_BILLING_LOGIC_APP
shipper_invoice_logic_app=$AZURE_ESTATE_SHIPPER_INVOICE_LOGIC_APP
service_bus="https://$AZURE_ESTATE_SERVICE_BUS_NAMESPACE.servicebus.windows.net"
echo "ERP fault Transport Order: $erp_fault_order"
echo "Shipper fault Transport Order: $shipper_fault_order"

app_setting() {
  az logicapp config appsettings list --subscription "$AZURE_SUBSCRIPTION_ID" --name "$1" --resource-group "$resource_group" \
    --query "[?name=='$2'].value | [0]" --output tsv
}
set_app_setting() {
  az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$1" --resource-group "$resource_group" \
    --settings "$2=$3" --output none
}

# Put back whatever a Logic App was calling, if the run ends while it is pointed away.
restore_erp=
restore_portal=
cleanup() {
  if [ -n "$restore_erp" ]; then
    set_app_setting "$billing_logic_app" ERP_BASE_URL "$restore_erp" \
      || echo "Put the Billing Logic App's ERP_BASE_URL back to $restore_erp" >&2
  fi
  if [ -n "$restore_portal" ]; then
    set_app_setting "$shipper_invoice_logic_app" PARTNER_PORTAL_BASE_URL "$restore_portal" \
      || echo "Put the Shipper invoice Logic App's PARTNER_PORTAL_BASE_URL back to $restore_portal" >&2
  fi
}
trap cleanup EXIT

workspace=$(az monitor log-analytics workspace show --resource-group "$resource_group" \
  --workspace-name "$AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE" --query customerId --output tsv)
logs() {
  az rest --method post --resource https://api.loganalytics.io --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
    --body "$(jq -n --arg query "$1" '{query: $query, timespan: "P1D"}')" --query 'tables[0].rows' --output json
}
# Ingestion takes a few minutes, so a check that finds nothing asks again, for up to 10 minutes.
until_found() {
  local result
  for _ in $(seq 1 30); do
    result=$("$@")
    [ -z "$result" ] || { echo "$result"; return; }
    sleep 20
  done
}
wait_for_presenter() {
  echo
  echo "$1"
  read -r -p "Press Enter once it is done... "
}

# The messages on the Billing subscription's dead-letter queue, as the presenter: each is locked and released again, never
# settled. One line each: {correlationId, reason}.
dead_letters() {
  local token headers location locations=()
  token=$(az account get-access-token --resource https://servicebus.azure.net --query accessToken --output tsv)
  for _ in $(seq 1 50); do
    headers=$(curl -sS -D - -o /dev/null -X POST \
      "$service_bus/transport-order-events/subscriptions/billing/\$deadletterqueue/messages/head?timeout=5" \
      -H "Authorization: Bearer $token" | tr -d '\r')
    head -1 <<<"$headers" | grep -q ' 201 ' || break
    locations+=("$(grep -i '^location:' <<<"$headers" | cut -d' ' -f2)")
    jq -nc \
      --arg correlationId "$(grep -i '^correlationid:' <<<"$headers" | cut -d' ' -f2- | tr -d '"')" \
      --arg reason "$(grep -i '^deadletterreason:' <<<"$headers" | cut -d' ' -f2- | tr -d '"')" \
      '{correlationId: $correlationId, reason: $reason}'
  done
  for location in "${locations[@]}"; do
    curl -sS -o /dev/null -X PUT "$location" -H "Authorization: Bearer $token"
  done
}

# Application Insights: the roles whose rows carry a value.
roles_with() {
  logs "union AppRequests, AppTraces, AppDependencies
| where tostring(Properties) contains '$1'
| summarize by AppRoleName" | jq -r '.[][0]'
}
found() {
  local what=$1 value=$2 roles missing
  shift 2
  for _ in $(seq 1 30); do
    roles=$(roles_with "$value")
    missing=
    for role in "$@"; do
      grep -qi "$role" <<<"$roles" || missing="$missing $role"
    done
    if [ -z "$missing" ]; then
      pass "$what is in Application Insights: $(tr '\n' ' ' <<<"$roles")"
      return
    fi
    sleep 20
  done
  fail "$what isn't in Application Insights for$missing after 10 minutes"
}
# The tracked hops of a document in a role, as "<outcome> <failureCode>", for the invoice or Credit note number.
hops_of() {
  logs "AppTraces
| where AppRoleName =~ '$2' and tostring(Properties.documentNumber) == '$1'
| project outcome = tostring(Properties.outcome), code = tostring(Properties.failureCode)
| distinct outcome, code" | jq -r '.[] | "\(.[0]) \(.[1])"'
}
await_hop() {
  local document=$1 role=$2 outcome=$3 code=${4:-} hops
  for _ in $(seq 1 30); do
    hops=$(hops_of "$document" "$role")
    grep -q "^$outcome $code" <<<"$hops" && return 0
    sleep 20
  done
  return 1
}

# 1. With the ERP billing API unreachable, the Billing Logic App retries and dead-letters TransportOrderBillable.
before=$(dead_letters | jq -r '.correlationId' | sort)
restore_erp=$(app_setting "$billing_logic_app" ERP_BASE_URL)
[ -n "$restore_erp" ] || fail "the Billing Logic App has no ERP_BASE_URL to put back"
echo
echo "Stopping the ERP billing API (pointing the Billing Logic App away from the ERP)..."
set_app_setting "$billing_logic_app" ERP_BASE_URL "https://erp.stopped.invalid"
sleep 60
wait_for_presenter "In the TMS UI ($tms_ui), make Transport Order $erp_fault_order Billable (release it, with its Proof of delivery in).
The Billing Logic App retries three times, about a minute in all, then dead-letters the message."
billable_id=
for _ in $(seq 1 30); do
  after=$(dead_letters | jq -r 'select(.reason == "ErpUnavailable") | .correlationId' | sort)
  billable_id=$(comm -13 <(echo "$before") <(echo "$after") | head -1)
  [ -z "$billable_id" ] || break
  sleep 10
done
[ -n "$billable_id" ] || fail "no new message on the Billing subscription's dead-letter queue with reason ErpUnavailable after 5 minutes"
pass "TransportOrderBillable of $erp_fault_order was retried and dead-lettered on the Billing subscription as ErpUnavailable (correlation ID $billable_id)"
wait_for_presenter "In the ERP UI ($erp_ui), open the Waiting lines and see that $erp_fault_order is not among them."
set_app_setting "$billing_logic_app" ERP_BASE_URL "$restore_erp"
restore_erp=
echo "The Billing Logic App calls the ERP again. Resubmit the dead-lettered message from the Integration console ($console/?key=$billable_id) to see the line arrive."

# 2. With the Shipper invoice endpoint unreachable, the invoice ends Not delivered.
restore_portal=$(app_setting "$shipper_invoice_logic_app" PARTNER_PORTAL_BASE_URL)
[ -n "$restore_portal" ] || fail "the Shipper invoice Logic App has no PARTNER_PORTAL_BASE_URL to put back"
echo
echo "Stopping the Shipper invoice endpoint (pointing the Logic App away from the Partner portal)..."
set_app_setting "$shipper_invoice_logic_app" PARTNER_PORTAL_BASE_URL "https://partner-portal.stopped.invalid"
sleep 60
wait_for_presenter "Make Transport Order $shipper_fault_order Billable the same way, and in the ERP UI ($erp_ui) choose Invoice now for its Shipper.
The Logic App retries three times, then reports the invoice Not delivered."
read -r -p "The invoice number the ERP issued (such as INV-2026-0001): " invoice
[[ $invoice =~ ^[A-Za-z0-9-]+$ ]] || fail "that isn't an invoice number"
await_hop "$invoice" "$shipper_invoice_logic_app" Failed ShipperUnavailable \
  || fail "invoice $invoice's send isn't tracked Failed with ShipperUnavailable after 10 minutes"
pass "invoice $invoice's send is tracked Failed with ShipperUnavailable after the retries"
[ -n "$(until_found hops_of "$invoice" erp-api)" ] \
  || fail "the ERP tracked nothing for invoice $invoice in Application Insights after 10 minutes"
wait_for_presenter "In the ERP UI, the Shipper invoices list shows $invoice as Not delivered, with the technical cause."

# 3. The endpoint is back, and Send again delivers it.
set_app_setting "$shipper_invoice_logic_app" PARTNER_PORTAL_BASE_URL "$restore_portal"
restore_portal=
sleep 60
wait_for_presenter "In the ERP UI, choose Send again on $invoice."
await_hop "$invoice" "$shipper_invoice_logic_app" Succeeded \
  || fail "invoice $invoice's send isn't tracked Succeeded after Send again, after 10 minutes"
pass "invoice $invoice was sent again and its send is tracked Succeeded"

# 4. The whole path, by correlation ID.
echo
found "the dead-lettered TransportOrderBillable, by correlation ID $billable_id," "$billable_id" "$billing_logic_app" tms-api
invoice_id=$(logs "AppTraces | where tostring(Properties.documentNumber) == '$invoice' and isnotempty(tostring(Properties.correlationId))
  | distinct tostring(Properties.correlationId)" | jq -r '.[0][0] // empty')
[ -n "$invoice_id" ] || fail "invoice $invoice has no correlation ID in Application Insights"
found "invoice $invoice, by correlation ID $invoice_id," "$invoice_id" erp "$shipper_invoice_logic_app" partner-portal
found "Transport Order $shipper_fault_order, by its number," "$shipper_fault_order" tms-api erp

echo
echo "Open in the ERP UI:"
echo "  Waiting lines and Shipper invoices: $erp_ui"
echo "Open in the Partner portal, as the Shipper's Persona (the script can't sign in as them):"
echo "  the invoices inbox: $invoice once, though it was sent twice"
echo "Open in the Integration console, by the Transport Order number and by each correlation ID:"
echo "  $erp_fault_order: $console/?key=$erp_fault_order"
echo "  $shipper_fault_order: $console/?key=$shipper_fault_order"
echo "  Dead-lettered message: $console/?key=$billable_id"
echo "  Invoice $invoice: $console/?key=$invoice_id"
