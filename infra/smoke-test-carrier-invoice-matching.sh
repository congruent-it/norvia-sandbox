#!/usr/bin/env bash
# Post-deploy presenter smoke run of Carrier invoice matching (E2.9, not in CI). It runs user story 15 (draft a Van Dijk
# Transport invoice for P9's delivered booking with an edited amount, submit, see Price mismatch in the ERP, accept at the agreed
# amount, see the outcome on Van Dijk's invoices page), a Shipper Persona refused with 403, and an outcome endpoint that is
# stopped, so the invoice is flagged Carrier not told. The faults are injected the way the other smoke runs do it, by pointing a
# Logic App at an address that doesn't answer and putting it back, because Logic Apps Standard has no way to stop one hop. The
# presenter's actions (the Partner portal as Van Dijk Transport, Finance in the ERP UI) are theirs: the script stops and says what
# to do. Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for, once the Carrier
# invoice and Carrier invoice outcome Logic Apps are deployed (azd up does it):
#
#   infra/smoke-test-carrier-invoice-matching.sh
#
# Get two Shipments of Van Dijk Transport through Freight settlement (docs/processes/tms/p9-freight-settlement.md): Confirmed,
# Delivered by the Persona's own Tracking events, with the Carrier costs recorded (they are in the ERP's Open Carrier costs). The
# Persona's draft invoice then offers both bookings. Do not invoice either first. It checks that:
#   1. story 15: the presenter's submission of the first booking at an edited amount reached the Carrier invoice Logic App and
#      was answered, the ERP tracked the invoice, and after Finance accepted the line at the agreed amount the outcome Logic App's
#      send to Van Dijk Transport is tracked Succeeded (the presenter looks at the ERP's Price mismatch, the accept and the
#      Persona's invoices page, and confirms);
#   2. a Shipper Persona (Brouwer Dranken) submitting a Carrier invoice gets 403 from API Management, and no Carrier invoice
#      Logic App run starts for it;
#   3. with the Carrier's invoice outcome endpoint unreachable for the outcome Logic App, the second booking's invoice at the
#      agreed amount (Matched, so Approved on receipt) ends Carrier not told: the send is tracked Failed with CarrierUnavailable
#      and the ERP recorded the report (the presenter looks at "Carrier not told" in the ERP UI and confirms);
#   4. the path is findable in Application Insights by correlation ID: the first invoice in API Management, the Carrier invoice
#      Logic App, the ERP, the outcome Logic App and the Partner portal.
# It prints identifiers only: never a body, a token or a key. What only a Persona's or Finance's page shows the script lists at
# the end for the presenter to open. The presenter's steps are in docs/processes/erp/e2-carrier-invoice-matching.md, "Presenter
# smoke run".
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[ -t 0 ] || fail "the presenter's actions are the presenter's, so run this in a terminal"

fqdn() { az containerapp show --name "$1" --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv; }
erp_ui="https://$(fqdn erp-web)"
portal="https://$(fqdn partner-portal-web)"
console="https://$(fqdn integration-console-web)"
# The estate has no output left for these two (Bicep allows 64), so the apps are found by their names.
logic_app_named() {
  az logicapp list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" \
    --query "[?starts_with(name, '$1')].name | [0]" --output tsv
}
invoice_logic_app=$(az logicapp list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" \
  --query "[?starts_with(name, 'logic-carrier-invoice-') && !starts_with(name, 'logic-carrier-invoice-outcome-')].name | [0]" --output tsv)
outcome_logic_app=$(logic_app_named logic-carrier-invoice-outcome-)
[ -n "$invoice_logic_app" ] && [ -n "$outcome_logic_app" ] || fail "the Carrier invoice and Carrier invoice outcome Logic Apps aren't deployed: azd up"
arm="https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers"
tenant=$(az account show --query tenantId --output tsv)
persona_client_id() { tr ';' '\n' <<<"$AZURE_ESTATE_PERSONA_CLIENT_IDS" | sed -n "s/^$1=//p"; }
persona_key() {
  az rest --method post --url "$arm/Microsoft.ApiManagement/service/$AZURE_ESTATE_API_MANAGEMENT/subscriptions/$1/listSecrets?api-version=2024-05-01" \
    --query primaryKey --output tsv
}

app_setting() {
  az logicapp config appsettings list --subscription "$AZURE_SUBSCRIPTION_ID" --name "$1" --resource-group "$resource_group" \
    --query "[?name=='$2'].value | [0]" --output tsv
}
set_app_setting() {
  az logicapp config appsettings set --subscription "$AZURE_SUBSCRIPTION_ID" --name "$1" --resource-group "$resource_group" \
    --settings "$2=$3" --output none
}

# Put back whatever the outcome Logic App was calling, and remove the throwaway job, if the run ends early.
restore_portal=
job_file=$(mktemp)
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  local job
  job=$(cat "$job_file")
  rm -f "$job_definition" "$job_file"
  [ -z "$job" ] || az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  if [ -n "$restore_portal" ]; then
    set_app_setting "$outcome_logic_app" PARTNER_PORTAL_BASE_URL "$restore_portal" \
      || echo "Put the Carrier invoice outcome Logic App's PARTNER_PORTAL_BASE_URL back to $restore_portal" >&2
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
ask_invoice_number() {
  local answer
  read -r -p "$1" answer
  [[ $answer =~ ^[A-Za-z0-9-]+$ ]] || fail "that isn't an invoice number"
  echo "$answer"
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
# The tracked hops of a Carrier invoice in a role, as "<outcome> <failureCode>", for the Carrier's invoice number.
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
correlation_id_of() {
  logs "AppTraces | where tostring(Properties.documentNumber) == '$1' and isnotempty(tostring(Properties.correlationId))
  | distinct tostring(Properties.correlationId)" | jq -r '.[0][0] // empty'
}

# A Shipper Persona's token can be had only as the Partner portal's identity (ADR 0005), so a throwaway Container Apps job
# submits a Carrier invoice as Brouwer Dranken and prints the status and correlation ID API Management answered with.
shipper_invoice_number="SMOKE-$(date +%Y%m%d%H%M%S)"
calls='set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
token=$(curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d grant_type=client_credentials -d client_id="$PERSONA_CLIENT_ID" -d scope="$PARTNER_API_CLIENT_ID/.default" \
  -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
  | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])")
status=$(curl -sS -o /dev/null -D /tmp/headers -w "%{http_code}" -X POST "$PARTNER_API/carrier-invoices" \
  -H "Authorization: Bearer $token" -H "Ocp-Apim-Subscription-Key: $PERSONA_KEY" -H "Content-Type: application/json" -d "$INVOICE")
correlation=$(grep -i "^x-correlation-id:" /tmp/headers | cut -d" " -f2- | tr -d "\r" || true)
echo "SMOKE-RESULT {\"status\": $status, \"correlationId\": \"$correlation\"}"'
shipper_attempt() {
  local job="smoke-carrier-invoice-$(date +%s)" execution status
  echo "$job" > "$job_file"
  jq -n \
    --arg location "$AZURE_LOCATION" --arg portalIdentity "$AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID" \
    --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
    --arg calls "$calls" --arg job "$job" --arg tenant "$tenant" --arg partnerApi "$AZURE_ESTATE_PARTNER_API_URL" \
    --arg partnerApiClientId "$AZURE_ESTATE_PARTNER_API_CLIENT_ID" --arg portalClientId "$AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID" \
    --arg personaClientId "$(persona_client_id brouwer-dranken)" --arg personaKey "$(persona_key brouwer-dranken)" \
    --arg invoice "$(jq -nc --arg number "$shipper_invoice_number" \
      '{invoiceNo: $number, invoiceDate: "20-10-2026", lines: [{bookingRef: "SMOKE-REF", amount: 1.00}]}')" '{
      location: $location,
      identity: { type: "UserAssigned", userAssignedIdentities: { ($portalIdentity): {} } },
      properties: {
        environmentId: $environment,
        configuration: {
          triggerType: "Manual", replicaTimeout: 600, replicaRetryLimit: 0,
          manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 },
          secrets: [ { name: "persona-key", value: $personaKey } ]
        },
        template: {
          containers: [{
            name: $job,
            image: "mcr.microsoft.com/azure-cli:latest",
            command: ["/bin/sh", "-c", $calls],
            env: [
              { name: "TENANT", value: $tenant },
              { name: "PARTNER_API", value: $partnerApi },
              { name: "PARTNER_API_CLIENT_ID", value: $partnerApiClientId },
              { name: "PARTNER_PORTAL_CLIENT_ID", value: $portalClientId },
              { name: "PERSONA_CLIENT_ID", value: $personaClientId },
              { name: "PERSONA_KEY", secretRef: "persona-key" },
              { name: "INVOICE", value: $invoice }
            ],
            resources: { cpu: 0.5, memory: "1Gi" }
          }]
        }
      }
    }' > "$job_definition"
  az containerapp job create --name "$job" --resource-group "$resource_group" --yaml "$job_definition" --output none
  execution=$(az containerapp job start --name "$job" --resource-group "$resource_group" --query name --output tsv)
  while
    status=$(az containerapp job execution show --name "$job" --resource-group "$resource_group" \
      --job-execution-name "$execution" --query properties.status --output tsv)
    [ "$status" = Running ] || [ "$status" = Processing ]
  do
    sleep 10
  done
  [ "$status" = Succeeded ] || fail "the Shipper Persona's call ended $status; see the logs of job execution $execution"
  job_result() {
    logs "ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$job' and Log_s startswith 'SMOKE-RESULT ' | project Log_s" \
      | jq -r '.[0][0] // empty' | sed 's/^SMOKE-RESULT //'
  }
  local found
  found=$(until_found job_result)
  [ -n "$found" ] || fail "the job's result didn't reach Log Analytics in 10 minutes"
  az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  : > "$job_file"
  echo "$found"
}

# Story 15. 1. Draft, submit, Price mismatch, accept at the agreed amount, and the outcome reaches Van Dijk's page.
echo
echo "Story 15"
wait_for_presenter "In the Partner portal ($portal), as Van Dijk Transport, open the draft invoice screen. It lists the delivered, Confirmed
bookings. Select the first booking, EDIT its amount (such as 20 EUR above the booking request's price), note a new invoice number and submit.
The answer shows the line as PRICE_MISMATCH."
invoice=$(ask_invoice_number "The invoice number you submitted (such as VDT-2026-0413): ")
await_hop "$invoice" "$invoice_logic_app" Succeeded \
  || fail "invoice $invoice has no Succeeded hop in the Carrier invoice Logic App after 10 minutes"
pass "invoice $invoice reached the Carrier invoice Logic App and the ERP answered"
[ -n "$(until_found hops_of "$invoice" erp-api)" ] \
  || fail "the ERP tracked nothing for invoice $invoice in Application Insights after 10 minutes"
pass "the ERP tracked invoice $invoice"
wait_for_presenter "In the ERP UI ($erp_ui/carrier-invoices/to-decide), as Finance, open $invoice: the line is Price mismatch, with its Carrier cost
next to it. Accept the line at the agreed amount (the Carrier cost's total) with the remark \"agreed by phone\"."
await_hop "$invoice" "$outcome_logic_app" Succeeded \
  || fail "invoice $invoice's outcome send isn't tracked Succeeded after Finance's accept, after 10 minutes"
pass "invoice $invoice is Approved and the outcome was sent to Van Dijk Transport (tracked Succeeded)"
wait_for_presenter "In the Partner portal, as Van Dijk Transport, open the invoices page: $invoice shows the outcome ACCEPTED_ADJUSTED at the agreed amount
with the remark, and the approved amount. In the ERP UI ($erp_ui/carrier-invoices) the Carrier told column has a time."
invoice_id=$(correlation_id_of "$invoice")
[ -n "$invoice_id" ] || fail "invoice $invoice has no correlation ID in Application Insights"

# 2. A Shipper Persona is refused at the edge.
echo
echo "Shipper Persona"
shipper_result=$(shipper_attempt)
jq -e '.status == 403' <<<"$shipper_result" >/dev/null \
  || fail "Brouwer Dranken's Carrier invoice got $(jq -r '.status' <<<"$shipper_result"), not 403"
refused_id=$(jq -r '.correlationId' <<<"$shipper_result")
[ -z "$(hops_of "$shipper_invoice_number" "$invoice_logic_app")" ] \
  || fail "the Carrier invoice Logic App tracked a run for the Shipper Persona's invoice $shipper_invoice_number"
pass "Brouwer Dranken's Carrier invoice is refused with 403 (correlation ID ${refused_id:-none}) and no Logic App run started"

# 3. With the Carrier's invoice outcome endpoint stopped, the invoice is flagged Carrier not told.
restore_portal=$(app_setting "$outcome_logic_app" PARTNER_PORTAL_BASE_URL)
[ -n "$restore_portal" ] || fail "the Carrier invoice outcome Logic App has no PARTNER_PORTAL_BASE_URL to put back"
echo
echo "Stopping the invoice outcome endpoint (pointing the outcome Logic App away from the Partner portal)..."
set_app_setting "$outcome_logic_app" PARTNER_PORTAL_BASE_URL "https://partner-portal.stopped.invalid"
sleep 60
wait_for_presenter "As Van Dijk Transport, draft an invoice for the second booking at the booking request's price (do not edit the amount), with a new
invoice number, and submit. The line is MATCHED, so the invoice is Approved on receipt. The outcome Logic App retries three times, about a
minute in all, then reports the outcome not delivered."
fault_invoice=$(ask_invoice_number "The invoice number you submitted: ")
await_hop "$fault_invoice" "$outcome_logic_app" Failed CarrierUnavailable \
  || fail "invoice $fault_invoice's outcome send isn't tracked Failed with CarrierUnavailable after 10 minutes"
pass "invoice $fault_invoice's outcome send is tracked Failed with CarrierUnavailable after the retries"
[ -n "$(until_found hops_of "$fault_invoice" erp-api)" ] \
  || fail "the ERP tracked nothing for invoice $fault_invoice in Application Insights after 10 minutes"
wait_for_presenter "In the ERP UI ($erp_ui/carrier-invoices), $fault_invoice is Approved and its Carrier told column says \"Carrier not told\", with the
technical cause (Finance would phone the Carrier)."
set_app_setting "$outcome_logic_app" PARTNER_PORTAL_BASE_URL "$restore_portal"
restore_portal=
echo "The outcome Logic App calls the Partner portal again."

# 4. The whole path, by correlation ID.
echo
found "invoice $invoice, by correlation ID $invoice_id," "$invoice_id" "$AZURE_ESTATE_API_MANAGEMENT" "$invoice_logic_app" erp partner-portal
found "invoice $invoice's outcome, by its number," "$invoice" "$outcome_logic_app" erp
found "the refused Shipper Persona's invoice, by correlation ID ${refused_id:-none}," "${refused_id:-$shipper_invoice_number}" "$AZURE_ESTATE_API_MANAGEMENT"

echo
echo "Open in the ERP UI:"
echo "  Carrier invoices (Carrier told, Carrier not told): $erp_ui/carrier-invoices"
echo "Open in the Partner portal, as Van Dijk Transport (the script can't sign in as the Persona):"
echo "  The invoices page: $invoice with its outcome per line"
echo "Open in the Integration console, by the invoice number and by the correlation ID:"
echo "  $invoice: $console/?key=$invoice"
echo "  Correlation ID: $console/?key=$invoice_id"
echo "  $fault_invoice: $console/?key=$fault_invoice"
