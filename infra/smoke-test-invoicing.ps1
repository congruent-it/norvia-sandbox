#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Invoicing, the fault injection and the tracing (E1.11, not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of Invoicing, the fault injection and the tracing (E1.11, not in CI). The happy path (Invoice
    now, Credit note, the Shipper's inbox) is played by the Simulator (docs/operations/simulator.md); this run keeps what it
    doesn't: the two faults and finding the path in Application Insights. The faults are injected the way the other smoke runs
    do it, by pointing a Logic App at an address that doesn't answer and putting it back, because Logic Apps Standard has no
    way to stop one hop. The presenter's actions (making an order Billable, Invoice now, Send again, in the TMS UI and the ERP
    UI) are theirs: the script stops and says what to do. Run it in a terminal, signed in to the Azure CLI as the presenter the
    estate was provisioned for, once the Billing and Shipper invoice Logic Apps are deployed (azd up does it):

      infra/smoke-test-invoicing.ps1 <Transport Order number for the ERP fault> <Transport Order number for the Shipper fault>

    Get two Transport Orders Delivered with a Proof of delivery, so that each becomes Billable the moment the presenter says so,
    for example by releasing them in the TMS UI. Use two Shippers, or invoice the first before the second is Billable: the
    second invoice must hold the second order's line. It checks that:
      1. with the ERP billing API unreachable for the Billing Logic App, the first order's TransportOrderBillable is retried and
         then dead-lettered on the Billing subscription, with ErpUnavailable as the reason, and the order's line is not among the
         ERP's Waiting lines (the presenter looks in the ERP UI and confirms);
      2. with the Shipper invoice endpoint unreachable for the Shipper invoice Logic App, the second order's invoice, issued by
         the presenter's Invoice now, ends Not delivered: the Logic App's send is tracked Failed with ShipperUnavailable and the
         ERP recorded the report;
      3. after the endpoint is back and the presenter's Send again, the invoice is Sent: the Logic App's send is tracked
         Succeeded and the ERP recorded the Sent report;
      4. the path is findable in Application Insights by correlation ID: the dead-lettered message in the Billing Logic App and
         the TMS, and the invoice in the ERP, the Shipper invoice Logic App and the Partner portal.
    It prints identifiers only: never a body, a token or a key. What only a Persona's or Finance's page shows (the Waiting lines,
    the Not delivered flag, the Shipper's inbox) the script lists at the end for the presenter to open. The presenter's steps are
    in docs/processes/erp/e1-invoicing.md, "Presenter smoke run".
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string] $ErpFaultOrder = '',
    [Parameter(Position = 1)][string] $ShipperFaultOrder = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID

if (-not ($ErpFaultOrder -match '^TO-[0-9]+$' -and $ShipperFaultOrder -match '^TO-[0-9]+$' -and $ErpFaultOrder -ne $ShipperFaultOrder)) {
    Write-Fail 'usage: smoke-test-invoicing.ps1 <Transport Order number for the ERP fault> <Transport Order number for the Shipper fault>, two different ones such as TO-0000042 TO-0000043'
}
if ([Console]::IsInputRedirected) { Write-Fail "the presenter's actions are the presenter's, so run this in a terminal" }

$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$erpUi = Get-AppUrl erp-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
$billingLogicApp = $env:AZURE_ESTATE_BILLING_LOGIC_APP
$shipperInvoiceLogicApp = $env:AZURE_ESTATE_SHIPPER_INVOICE_LOGIC_APP
$namespace = $env:AZURE_ESTATE_SERVICE_BUS_NAMESPACE
Write-Host "ERP fault Transport Order: $ErpFaultOrder"
Write-Host "Shipper fault Transport Order: $ShipperFaultOrder"

function Get-AppSetting([string] $App, [string] $Name) {
    (Invoke-Az logicapp config appsettings list --subscription $subscription --name $App --resource-group $resourceGroup `
            --query "[?name=='$Name'].value | [0]" --output tsv | Out-String).Trim()
}
function Set-AppSetting([string] $App, [string] $Name, [string] $Value) {
    Invoke-Az logicapp config appsettings set --subscription $subscription --name $App --resource-group $resourceGroup `
        --settings "$Name=$Value" --output none | Out-Null
}

$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup
# The messages on the Billing subscription's dead-letter queue, as the presenter: each is locked and released again, never settled.
function Get-BillingDeadLetters { Get-DeadLetters -Namespace $namespace -Queue 'transport-order-events/subscriptions/billing' }

# Put back whatever a Logic App was calling, if the run ends while it is pointed away.
$restoreErp = $null
$restorePortal = $null
try {
    # 1. With the ERP billing API unreachable, the Billing Logic App retries and dead-letters TransportOrderBillable.
    $before = @(Get-BillingDeadLetters | ForEach-Object CorrelationId)
    $restoreErp = Get-AppSetting $billingLogicApp ERP_BASE_URL
    if (-not $restoreErp) { Write-Fail 'the Billing Logic App has no ERP_BASE_URL to put back' }
    Write-Host ''
    Write-Host 'Stopping the ERP billing API (pointing the Billing Logic App away from the ERP)...'
    Set-AppSetting $billingLogicApp ERP_BASE_URL 'https://erp.stopped.invalid'
    Start-Sleep -Seconds 60
    Wait-ForPresenter "In the TMS UI ($tmsUi), make Transport Order $ErpFaultOrder Billable (release it, with its Proof of delivery in).`nThe Billing Logic App retries three times, about a minute in all, then dead-letters the message."
    $billableId = Wait-Until -Attempts 30 -IntervalSeconds 10 {
        $new = @(Get-BillingDeadLetters | Where-Object { $_.Reason -eq 'ErpUnavailable' -and $_.CorrelationId -notin $before } | ForEach-Object CorrelationId)
        $new.Count -gt 0 ? $new[0] : $null
    }
    if (-not $billableId) { Write-Fail "no new message on the Billing subscription's dead-letter queue with reason ErpUnavailable after 5 minutes" }
    Write-Pass "TransportOrderBillable of $ErpFaultOrder was retried and dead-lettered on the Billing subscription as ErpUnavailable (correlation ID $billableId)"
    Wait-ForPresenter "In the ERP UI ($erpUi), open the Waiting lines and see that $ErpFaultOrder is not among them."
    Set-AppSetting $billingLogicApp ERP_BASE_URL $restoreErp
    $restoreErp = $null
    Write-Host "The Billing Logic App calls the ERP again. Resubmit the dead-lettered message from the Integration console ($console/?key=$billableId) to see the line arrive."

    # 2. With the Shipper invoice endpoint unreachable, the invoice ends Not delivered.
    $restorePortal = Get-AppSetting $shipperInvoiceLogicApp PARTNER_PORTAL_BASE_URL
    if (-not $restorePortal) { Write-Fail 'the Shipper invoice Logic App has no PARTNER_PORTAL_BASE_URL to put back' }
    Write-Host ''
    Write-Host 'Stopping the Shipper invoice endpoint (pointing the Logic App away from the Partner portal)...'
    Set-AppSetting $shipperInvoiceLogicApp PARTNER_PORTAL_BASE_URL 'https://partner-portal.stopped.invalid'
    Start-Sleep -Seconds 60
    Wait-ForPresenter "Make Transport Order $ShipperFaultOrder Billable the same way, and in the ERP UI ($erpUi) choose Invoice now for its Shipper.`nThe Logic App retries three times, then reports the invoice Not delivered."
    $invoice = Read-DocumentNumber 'The invoice number the ERP issued (such as INV-2026-0001): '
    if (-not (Wait-DocumentHop -WorkspaceId $workspace -Document $invoice -Role $shipperInvoiceLogicApp -Outcome Failed -Code ShipperUnavailable)) {
        Write-Fail "invoice $invoice's send isn't tracked Failed with ShipperUnavailable after 10 minutes"
    }
    Write-Pass "invoice $invoice's send is tracked Failed with ShipperUnavailable after the retries"
    if (-not (Wait-Until { Get-DocumentHops -WorkspaceId $workspace -Document $invoice -Role erp-api })) {
        Write-Fail "the ERP tracked nothing for invoice $invoice in Application Insights after 10 minutes"
    }
    Wait-ForPresenter "In the ERP UI, the Shipper invoices list shows $invoice as Not delivered, with the technical cause."

    # 3. The endpoint is back, and Send again delivers it.
    Set-AppSetting $shipperInvoiceLogicApp PARTNER_PORTAL_BASE_URL $restorePortal
    $restorePortal = $null
    Start-Sleep -Seconds 60
    Wait-ForPresenter "In the ERP UI, choose Send again on $invoice."
    if (-not (Wait-DocumentHop -WorkspaceId $workspace -Document $invoice -Role $shipperInvoiceLogicApp -Outcome Succeeded)) {
        Write-Fail "invoice $invoice's send isn't tracked Succeeded after Send again, after 10 minutes"
    }
    Write-Pass "invoice $invoice was sent again and its send is tracked Succeeded"

    # 4. The whole path, by correlation ID.
    Write-Host ''
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the dead-lettered TransportOrderBillable, by correlation ID $billableId," -Value $billableId -Roles $billingLogicApp, 'tms-api'
    $invoiceId = Wait-Until {
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "AppTraces | where tostring(Properties.documentNumber) == '$invoice' and isnotempty(tostring(Properties.correlationId)) | distinct tostring(Properties.correlationId)"
        $rows.Count -gt 0 ? $rows[0][0] : $null
    }
    if (-not $invoiceId) { Write-Fail "invoice $invoice has no correlation ID in Application Insights" }
    Assert-InApplicationInsights -WorkspaceId $workspace -What "invoice $invoice, by correlation ID $invoiceId," -Value $invoiceId -Roles 'erp', $shipperInvoiceLogicApp, 'partner-portal'
    Assert-InApplicationInsights -WorkspaceId $workspace -What "Transport Order $ShipperFaultOrder, by its number," -Value $ShipperFaultOrder -Roles 'tms-api', 'erp'

    Write-Host ''
    Write-Host 'Open in the ERP UI:'
    Write-Host "  Waiting lines and Shipper invoices: $erpUi"
    Write-Host "Open in the Partner portal, as the Shipper's Persona (the script can't sign in as them):"
    Write-Host "  the invoices inbox: $invoice once, though it was sent twice"
    Write-Host 'Open in the Integration console, by the Transport Order number and by each correlation ID:'
    Write-Host "  ${ErpFaultOrder}: $console/?key=$ErpFaultOrder"
    Write-Host "  ${ShipperFaultOrder}: $console/?key=$ShipperFaultOrder"
    Write-Host "  Dead-lettered message: $console/?key=$billableId"
    Write-Host "  Invoice ${invoice}: $console/?key=$invoiceId"
}
finally {
    if ($restoreErp) {
        try { Set-AppSetting $billingLogicApp ERP_BASE_URL $restoreErp }
        catch { Write-Host "Put the Billing Logic App's ERP_BASE_URL back to $restoreErp" }
    }
    if ($restorePortal) {
        try { Set-AppSetting $shipperInvoiceLogicApp PARTNER_PORTAL_BASE_URL $restorePortal }
        catch { Write-Host "Put the Shipper invoice Logic App's PARTNER_PORTAL_BASE_URL back to $restorePortal" }
    }
}
