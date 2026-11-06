#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of the "contrast" re-enactment (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set, signed in
.DESCRIPTION
    Post-deploy presenter smoke run of the "contrast" re-enactment (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set, signed in
    to the Azure CLI as the presenter the estate was provisioned for, once Shipment tracking works (P5, P8, P12):

      infra/smoke-test-as-found-contrast.ps1 <Transport Order number of a Shipment that is In transit>

    It checks that:
      1. with the contrast preset (the healthy estate), the tracking flow's failed-run and dead-letter alerts exist;
      2. with the fault tms-shipper-tracking-api-failing on, the Shipper's read fails at once and loudly (503, nothing queued), and the
         fault tms-tracking-event-api-failing on, a Carrier's Tracking event is accepted with 202 and no message is dead-lettered;
      3. once the faults have expired or been switched off, nothing is lost: the Carrier's event is recorded, the Shipment moved,
         and the tracking dead-letter queue holds nothing new.
    The presenter's observations are asked as yes or no. It ends in the fixed state.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Number = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$queue = 'carrier-tracking'

if ($Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-as-found-contrast.ps1 <Transport Order number>, such as TO-0000042' }
if ([Console]::IsInputRedirected) { Write-Fail "the Shipper's and the Carrier's sides are the presenter's, so run this in a terminal" }

$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
function Get-DeadLetterCount { Get-QueueDeadLetterCount -Namespace $env:AZURE_ESTATE_SERVICE_BUS_NAMESPACE -Queue $queue -ResourceGroup $resourceGroup }
function Confirm-Observation([string] $Question, [string] $Failure, [string] $Success) {
    if (-not (Read-Confirmation $Question)) { Write-Fail $Failure }
    Write-Pass $Success
}

try {
    Write-Host '1. The contrast preset'
    Invoke-AsFound contrast
    foreach ($name in 'alert-failed-runs-carrier-tracking', 'alert-dead-letter-carrier-tracking') {
        if ((Get-MetricAlertValue $name enabled) -ne 'true') { Write-Fail "$name is missing or disabled" }
        Write-Pass "$name exists"
    }

    Write-Host ''
    Write-Host '2. The TMS fails for a few minutes'
    $before = Get-DeadLetterCount
    Write-Host @"
  1. Open the presenter panel ($portal/presenter) and switch on tms-shipper-tracking-api-failing and tms-tracking-event-api-failing
     for 2 minutes.
  2. As the Shipper of Transport Order $Number, read its tracking in the Partner portal ($portal).
  3. As the Carrier, send the next Tracking event for it.
"@
    Read-Host 'Press Enter when you have done both. ' | Out-Null
    Confirm-Observation 'Did the Shipper see an error at once (503)?' "the Shipper's read did not fail loudly" 'the failure is loud: the Shipper sees the error'
    Confirm-Observation 'Was the Carrier told 202 Accepted?' 'the Carrier was not told 202' "the Carrier's event is accepted and queued"
    Write-Host "  4. In the Integration console ($console/?key=$Number) find the TMS hops: Failed, with the fault."
    Confirm-Observation 'Did the console show the failed hops with the fault?' 'the console did not show the fault' 'the Integration console shows the failure and its cause'

    Write-Host ''
    Write-Host '3. The TMS is back and nothing is lost'
    Write-Host '  5. Wait for the faults to expire (or switch them off in the presenter panel), then wait for the event to be recorded.'
    Read-Host 'Press Enter when the faults are off. ' | Out-Null
    for ($i = 0; $i -lt 24 -and (Get-DeadLetterCount) -eq $before; $i++) { Start-Sleep -Seconds 5 }
    if ((Get-DeadLetterCount) -ne $before) { Write-Fail "the $queue dead-letter queue grew: a message was lost to the dead-letter queue" }
    Write-Pass 'nothing was dead-lettered'
    Confirm-Observation "Is the Carrier's event now on the Shipment's timeline, and the Shipment moved on?" 'the event was lost' 'nothing was lost: the event is recorded and the Shipment moved'
    Confirm-Observation "Does the Shipper's read work again?" "the Shipper's read still fails" "the Shipper's read works again"
    Write-Host 'Done: the estate is in the fixed state.'
}
finally {
    try { Invoke-AsFound fixed | Out-Null }
    catch { Write-Host 'Put the estate back with infra/as-found.sh fixed' }
}
