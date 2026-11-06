#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of the "silent Carrier" re-enactment (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set,
.DESCRIPTION
    Post-deploy presenter smoke run of the "silent Carrier" re-enactment (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set,
    signed in to the Azure CLI as the presenter the estate was provisioned for, once Shipment tracking works (P5):

      infra/smoke-test-as-found-silent-carrier.ps1 <Transport Order number of a Shipment that is In transit>

    It checks that:
      1. with the silent-carrier preset, the Carrier-tracking flow has no failed-run alert and its queue no dead-letter alert,
         another flow still has both, and the TMS API has no Stale thresholds;
      2. with the fault carrier-tracking-unmapped-code on (the presenter switches it in the Partner portal's presenter panel and
         sends a Delivered event as the Carrier), the Carrier is told 202, the tracking queue's dead-letter queue holds the
         message with UnmappableTrackingMessage, and the Shipment is still In transit;
      3. with the fixed preset, the failed-run and dead-letter alerts exist for the flow and go to an action group with a receiver
         (who is told), and the Stale thresholds are set.
    The presenter's observations (the Carrier's 202, the Shipper's screen, the Integration console's trail) are asked as yes or no.
    It ends in the fixed state.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Number = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$queue = 'carrier-tracking'

if ($Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-as-found-silent-carrier.ps1 <Transport Order number>, such as TO-0000042' }
if ([Console]::IsInputRedirected) { Write-Fail "the Carrier's and the Shipper's sides are the presenter's, so run this in a terminal" }
if (-not $env:ESTATE_ALERT_EMAIL) { Write-Fail 'set the receiver first: azd env set ESTATE_ALERT_EMAIL <address>' }

$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup

function Get-TmsSetting([string] $Name) {
    (Invoke-Az containerapp show --name tms-api --resource-group $resourceGroup `
            --query "properties.template.containers[0].env[?name == '$Name'].value | [0]" --output tsv | Out-String).Trim()
}
function Get-DeadLetterCount { Get-QueueDeadLetterCount -Namespace $env:AZURE_ESTATE_SERVICE_BUS_NAMESPACE -Queue $queue -ResourceGroup $resourceGroup }
function Confirm-Observation([string] $Question, [string] $Failure, [string] $Success) {
    if (-not (Read-Confirmation $Question)) { Write-Fail $Failure }
    Write-Pass $Success
}

try {
    Write-Host '1. The silent-carrier preset'
    Invoke-AsFound silent-carrier
    if ($null -ne (Get-MetricAlertValue alert-failed-runs-carrier-tracking name)) { Write-Fail 'alert-failed-runs-carrier-tracking exists' }
    Write-Pass 'the Carrier-tracking flow has no failed-run alert'
    if ($null -ne (Get-MetricAlertValue alert-dead-letter-carrier-tracking name)) { Write-Fail 'alert-dead-letter-carrier-tracking exists' }
    Write-Pass 'the tracking queue has no dead-letter alert'
    if ($null -eq (Get-MetricAlertValue alert-failed-runs-carrier-booking name)) { Write-Fail 'another flow lost its alert' }
    Write-Pass 'another flow (carrier-booking) still has its failed-run alert'
    if ((Get-TmsSetting ShipmentStale__ThresholdsSet) -ne 'false') { Write-Fail 'the Stale thresholds are set under silent-carrier' }
    Write-Pass 'the TMS API has no Stale thresholds'

    Write-Host ''
    Write-Host '2. The Carrier posts "delivered"'
    $before = Get-DeadLetterCount
    Write-Host @"
  1. Open the presenter panel ($portal/presenter) and switch on carrier-tracking-unmapped-code for 5 minutes.
  2. As the Carrier of Transport Order $Number, send its Delivered event in the Partner portal ($portal).
"@
    Read-Host 'Press Enter when the Carrier has sent it. ' | Out-Null
    Confirm-Observation 'Was the Carrier told 202 Accepted?' 'the Carrier was not told 202' 'the Carrier is told 202 Accepted'
    for ($i = 0; $i -lt 24 -and (Get-DeadLetterCount) -le $before; $i++) { Start-Sleep -Seconds 10 }
    if ((Get-DeadLetterCount) -le $before) { Write-Fail "the $queue dead-letter queue did not grow after 4 minutes" }
    Write-Pass "the $queue dead-letter queue holds the message"
    Confirm-Observation "Is the Shipper's screen for $Number still on In transit?" "the Shipper's screen moved on" "the Shipper's screen stays on In transit"
    Write-Host "  3. Open the Integration console ($console/?key=$Number), find the failed run and the dead-lettered message (UnmappableTrackingMessage)."
    Confirm-Observation 'Did the console show the dead-lettered message and the fault?' 'the console did not find the message' 'the Integration console finds the dead-lettered message'
    Write-Host '  4. Switch the fault off in the presenter panel.'
    Read-Host 'Press Enter when it is off. ' | Out-Null

    Write-Host ''
    Write-Host '3. The fixed run'
    Invoke-AsFound fixed
    foreach ($name in 'alert-failed-runs-carrier-tracking', 'alert-dead-letter-carrier-tracking') {
        if ((Get-MetricAlertValue $name enabled) -ne 'true') { Write-Fail "$name is missing or disabled under the fixed preset" }
        if ([int] (Get-MetricAlertValue $name 'length(actions)') -lt 1) { Write-Fail "$name sends nowhere" }
        Write-Pass "$name exists and sends to an action group"
    }
    $receivers = [int] (Invoke-Az monitor action-group list --resource-group $resourceGroup --query "[?starts_with(name, 'ag-norvia-')] | [0].emailReceivers | length(@)" --output tsv | Out-String).Trim()
    if ($receivers -lt 1) { Write-Fail 'the action group has no receiver' }
    Write-Pass 'the action group has an email receiver: a run of the fault now tells someone'
    if ((Get-TmsSetting ShipmentStale__ThresholdsSet) -ne 'true') { Write-Fail 'the Stale thresholds are not set under the fixed preset' }
    Write-Pass 'the TMS API has its Stale thresholds'
    Write-Host 'Done: the estate is in the fixed state. Resubmit the dead-lettered message from the console to clear the dead-letter alert.'
}
finally {
    try { Invoke-AsFound fixed | Out-Null }
    catch { Write-Host 'Put the estate back with infra/as-found.ps1 fixed' }
}
