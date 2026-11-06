#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Carrier invoice matching (E2.9, not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of Carrier invoice matching (E2.9, not in CI). It runs user story 15 (draft a Van Dijk
    Transport invoice for P9's delivered booking with an edited amount, submit, see Price mismatch in the ERP, accept at the agreed
    amount, see the outcome on Van Dijk's invoices page), a Shipper Persona refused with 403, and an outcome endpoint that is
    stopped, so the invoice is flagged Carrier not told. The faults are injected the way the other smoke runs do it, by pointing a
    Logic App at an address that doesn't answer and putting it back, because Logic Apps Standard has no way to stop one hop. The
    presenter's actions (the Partner portal as Van Dijk Transport, Finance in the ERP UI) are theirs: the script stops and says what
    to do. Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for, once the Carrier
    invoice and Carrier invoice outcome Logic Apps are deployed (azd up does it):

      infra/smoke-test-carrier-invoice-matching.ps1

    Get two Shipments of Van Dijk Transport through Freight settlement (docs/processes/tms/p9-freight-settlement.md): Confirmed,
    Delivered by the Persona's own Tracking events, with the Carrier costs recorded (they are in the ERP's Open Carrier costs). The
    Persona's draft invoice then offers both bookings. Do not invoice either first. It checks that:
      1. story 15: the presenter's submission of the first booking at an edited amount reached the Carrier invoice Logic App and
         was answered, the ERP tracked the invoice, and after Finance accepted the line at the agreed amount the outcome Logic App's
         send to Van Dijk Transport is tracked Succeeded (the presenter looks at the ERP's Price mismatch, the accept and the
         Persona's invoices page, and confirms);
      2. a Shipper Persona (Brouwer Dranken) submitting a Carrier invoice gets 403 from API Management, and no Carrier invoice
         Logic App run starts for it;
      3. with the Carrier's invoice outcome endpoint unreachable for the outcome Logic App, the second booking's invoice at the
         agreed amount (Matched, so Approved on receipt) ends Carrier not told: the send is tracked Failed with CarrierUnavailable
         and the ERP recorded the report (the presenter looks at "Carrier not told" in the ERP UI and confirms);
      4. the path is findable in Application Insights by correlation ID: the first invoice in API Management, the Carrier invoice
         Logic App, the ERP, the outcome Logic App and the Partner portal.
    It prints identifiers only: never a body, a token or a key. What only a Persona's or Finance's page shows the script lists at
    the end for the presenter to open. The presenter's steps are in docs/processes/erp/e2-carrier-invoice-matching.md, "Presenter
    smoke run".
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID

if ([Console]::IsInputRedirected) { Write-Fail "the presenter's actions are the presenter's, so run this in a terminal" }

$erpUi = Get-AppUrl erp-web -ResourceGroup $resourceGroup
$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
# The estate has no output left for these two (Bicep allows 64), so the apps are found by their names.
$invoiceLogicApp = (Invoke-Az logicapp list --subscription $subscription --resource-group $resourceGroup `
        --query "[?starts_with(name, 'logic-carrier-invoice-') && !starts_with(name, 'logic-carrier-invoice-outcome-')].name | [0]" --output tsv | Out-String).Trim()
$outcomeLogicApp = (Invoke-Az logicapp list --subscription $subscription --resource-group $resourceGroup `
        --query "[?starts_with(name, 'logic-carrier-invoice-outcome-')].name | [0]" --output tsv | Out-String).Trim()
if (-not ($invoiceLogicApp -and $outcomeLogicApp)) { Write-Fail "the Carrier invoice and Carrier invoice outcome Logic Apps aren't deployed: azd up" }
$arm = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers"
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
function Get-PersonaKey([string] $Persona) {
    (Invoke-Az rest --method post --url "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/$Persona/listSecrets?api-version=2024-05-01" `
            --query primaryKey --output tsv | Out-String).Trim()
}

function Get-AppSetting([string] $App, [string] $Name) {
    (Invoke-Az logicapp config appsettings list --subscription $subscription --name $App --resource-group $resourceGroup `
            --query "[?name=='$Name'].value | [0]" --output tsv | Out-String).Trim()
}
function Set-AppSetting([string] $App, [string] $Name, [string] $Value) {
    Invoke-Az logicapp config appsettings set --subscription $subscription --name $App --resource-group $resourceGroup `
        --settings "$Name=$Value" --output none | Out-Null
}

$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup
function Wait-Hop([string] $Document, [string] $Role, [string] $Outcome, [string] $Code = '') {
    Wait-DocumentHop -WorkspaceId $workspace -Document $Document -Role $Role -Outcome $Outcome -Code $Code
}
function Get-Hops([string] $Document, [string] $Role) { Get-DocumentHops -WorkspaceId $workspace -Document $Document -Role $Role }
function Get-DocumentCorrelationId([string] $Document) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
AppTraces | where tostring(Properties.documentNumber) == '$Document' and isnotempty(tostring(Properties.correlationId))
| distinct tostring(Properties.correlationId)
"@
    $rows.Count -gt 0 ? $rows[0][0] : $null
}

# A Shipper Persona's token can be had only as the Partner portal's identity (ADR 0005), so a throwaway Container Apps job
# submits a Carrier invoice as Brouwer Dranken and returns the status and correlation ID API Management answered with.
$shipperInvoiceNumber = "SMOKE-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
$calls = @'
set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
token=$(curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d grant_type=client_credentials -d client_id="$PERSONA_CLIENT_ID" -d scope="$PARTNER_API_CLIENT_ID/.default" \
  -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
  | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])")
status=$(curl -sS -o /dev/null -D /tmp/headers -w "%{http_code}" -X POST "$PARTNER_API/carrier-invoices" \
  -H "Authorization: Bearer $token" -H "Ocp-Apim-Subscription-Key: $PERSONA_KEY" -H "Content-Type: application/json" -d "$INVOICE")
correlation=$(grep -i "^x-correlation-id:" /tmp/headers | cut -d" " -f2- | tr -d "\r" || true)
echo "SMOKE-RESULT {\"status\": $status, \"correlationId\": \"$correlation\"}"
'@
function Invoke-ShipperAttempt {
    $invoice = @{ invoiceNo = $shipperInvoiceNumber; invoiceDate = '20-10-2026'; lines = @(@{ bookingRef = 'SMOKE-REF'; amount = 1.00 }) } | ConvertTo-Json -Compress -Depth 5
    Invoke-ThrowawayJobForResult -Prefix smoke-carrier-invoice -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 600 `
        -Purpose "the Shipper Persona's call" -IdentityId $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID `
        -Secrets ([ordered]@{ PERSONA_KEY = Get-PersonaKey brouwer-dranken }) -Environment ([ordered]@{
            TENANT                   = $tenant
            PARTNER_API              = $env:AZURE_ESTATE_PARTNER_API_URL
            PARTNER_API_CLIENT_ID    = $env:AZURE_ESTATE_PARTNER_API_CLIENT_ID
            PARTNER_PORTAL_CLIENT_ID = $env:AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID
            PERSONA_CLIENT_ID        = Get-PersonaClientId brouwer-dranken
            INVOICE                  = $invoice
        })
}

# Put back whatever the outcome Logic App was calling, if the run ends early.
$restorePortal = $null
try {
    # Story 15. 1. Draft, submit, Price mismatch, accept at the agreed amount, and the outcome reaches Van Dijk's page.
    Write-Host ''
    Write-Host 'Story 15'
    Wait-ForPresenter "In the Partner portal ($portal), as Van Dijk Transport, open the draft invoice screen. It lists the delivered, Confirmed`nbookings. Select the first booking, EDIT its amount (such as 20 EUR above the booking request's price), note a new invoice number and submit.`nThe answer shows the line as PRICE_MISMATCH."
    $invoice = Read-DocumentNumber 'The invoice number you submitted (such as VDT-2026-0413): '
    if (-not (Wait-Hop $invoice $invoiceLogicApp Succeeded)) { Write-Fail "invoice $invoice has no Succeeded hop in the Carrier invoice Logic App after 10 minutes" }
    Write-Pass "invoice $invoice reached the Carrier invoice Logic App and the ERP answered"
    if (-not (Wait-Until { Get-Hops $invoice erp-api })) { Write-Fail "the ERP tracked nothing for invoice $invoice in Application Insights after 10 minutes" }
    Write-Pass "the ERP tracked invoice $invoice"
    Wait-ForPresenter "In the ERP UI ($erpUi/carrier-invoices/to-decide), as Finance, open ${invoice}: the line is Price mismatch, with its Carrier cost`nnext to it. Accept the line at the agreed amount (the Carrier cost's total) with the remark `"agreed by phone`"."
    if (-not (Wait-Hop $invoice $outcomeLogicApp Succeeded)) { Write-Fail "invoice $invoice's outcome send isn't tracked Succeeded after Finance's accept, after 10 minutes" }
    Write-Pass "invoice $invoice is Approved and the outcome was sent to Van Dijk Transport (tracked Succeeded)"
    Wait-ForPresenter "In the Partner portal, as Van Dijk Transport, open the invoices page: $invoice shows the outcome ACCEPTED_ADJUSTED at the agreed amount`nwith the remark, and the approved amount. In the ERP UI ($erpUi/carrier-invoices) the Carrier told column has a time."
    $invoiceId = Get-DocumentCorrelationId $invoice
    if (-not $invoiceId) { Write-Fail "invoice $invoice has no correlation ID in Application Insights" }

    # 2. A Shipper Persona is refused at the edge.
    Write-Host ''
    Write-Host 'Shipper Persona'
    $shipperResult = Invoke-ShipperAttempt
    if ($shipperResult.status -ne 403) { Write-Fail "Brouwer Dranken's Carrier invoice got $($shipperResult.status), not 403" }
    $refusedId = $shipperResult.correlationId
    if (Get-Hops $shipperInvoiceNumber $invoiceLogicApp) { Write-Fail "the Carrier invoice Logic App tracked a run for the Shipper Persona's invoice $shipperInvoiceNumber" }
    Write-Pass "Brouwer Dranken's Carrier invoice is refused with 403 (correlation ID $($refusedId ? $refusedId : 'none')) and no Logic App run started"

    # 3. With the Carrier's invoice outcome endpoint stopped, the invoice is flagged Carrier not told.
    $portalUrl = Get-AppSetting $outcomeLogicApp PARTNER_PORTAL_BASE_URL
    if (-not $portalUrl) { Write-Fail 'the Carrier invoice outcome Logic App has no PARTNER_PORTAL_BASE_URL to put back' }
    Write-Host ''
    Write-Host 'Stopping the invoice outcome endpoint (pointing the outcome Logic App away from the Partner portal)...'
    $restorePortal = $portalUrl
    Set-AppSetting $outcomeLogicApp PARTNER_PORTAL_BASE_URL 'https://partner-portal.stopped.invalid'
    Start-Sleep -Seconds 60
    Wait-ForPresenter "As Van Dijk Transport, draft an invoice for the second booking at the booking request's price (do not edit the amount), with a new`ninvoice number, and submit. The line is MATCHED, so the invoice is Approved on receipt. The outcome Logic App retries three times, about a`nminute in all, then reports the outcome not delivered."
    $faultInvoice = Read-DocumentNumber 'The invoice number you submitted: '
    if (-not (Wait-Hop $faultInvoice $outcomeLogicApp Failed CarrierUnavailable)) { Write-Fail "invoice $faultInvoice's outcome send isn't tracked Failed with CarrierUnavailable after 10 minutes" }
    Write-Pass "invoice $faultInvoice's outcome send is tracked Failed with CarrierUnavailable after the retries"
    if (-not (Wait-Until { Get-Hops $faultInvoice erp-api })) { Write-Fail "the ERP tracked nothing for invoice $faultInvoice in Application Insights after 10 minutes" }
    Wait-ForPresenter "In the ERP UI ($erpUi/carrier-invoices), $faultInvoice is Approved and its Carrier told column says `"Carrier not told`", with the`ntechnical cause (Finance would phone the Carrier)."
    Set-AppSetting $outcomeLogicApp PARTNER_PORTAL_BASE_URL $portalUrl
    $restorePortal = $null
    Write-Host 'The outcome Logic App calls the Partner portal again.'

    # 4. The whole path, by correlation ID.
    Write-Host ''
    $apim = $env:AZURE_ESTATE_API_MANAGEMENT
    Assert-InApplicationInsights -WorkspaceId $workspace -What "invoice $invoice, by correlation ID $invoiceId," -Value $invoiceId -Roles $apim, $invoiceLogicApp, 'erp', 'partner-portal'
    Assert-InApplicationInsights -WorkspaceId $workspace -What "invoice $invoice's outcome, by its number," -Value $invoice -Roles $outcomeLogicApp, 'erp'
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the refused Shipper Persona's invoice, by correlation ID $($refusedId ? $refusedId : 'none')," `
        -Value ($refusedId ? $refusedId : $shipperInvoiceNumber) -Roles $apim

    Write-Host ''
    Write-Host 'Open in the ERP UI:'
    Write-Host "  Carrier invoices (Carrier told, Carrier not told): $erpUi/carrier-invoices"
    Write-Host "Open in the Partner portal, as Van Dijk Transport (the script can't sign in as the Persona):"
    Write-Host "  The invoices page: $invoice with its outcome per line"
    Write-Host 'Open in the Integration console, by the invoice number and by the correlation ID:'
    Write-Host "  ${invoice}: $console/?key=$invoice"
    Write-Host "  Correlation ID: $console/?key=$invoiceId"
    Write-Host "  ${faultInvoice}: $console/?key=$faultInvoice"
}
finally {
    if ($restorePortal) {
        try { Set-AppSetting $outcomeLogicApp PARTNER_PORTAL_BASE_URL $restorePortal }
        catch { Write-Host "Put the Carrier invoice outcome Logic App's PARTNER_PORTAL_BASE_URL back to $restorePortal" }
    }
}
