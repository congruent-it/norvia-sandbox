#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of the "look-alike" re-enactment (not in CI), after `azd up`, signed in to the Azure CLI as the
.DESCRIPTION
    Post-deploy presenter smoke run of the "look-alike" re-enactment (not in CI), after `azd up`, signed in to the Azure CLI as the
    presenter the estate was provisioned for, once Shipment tracking works (P5, P8):

      infra/smoke-test-as-found-look-alike.ps1 <Transport Order number of a Shipment that is In transit>

    It checks that:
      1. with the look-alike preset, the Shipper tracking policy of API Management holds the cache (none under fixed), and no
         alert, fault or Stale threshold is changed: the estate is otherwise healthy;
      2. the TMS is right and the Shipper sees an old status: after the Shipper has read the Transport Order and the Carrier has
         moved the Shipment on, the Partner portal's Shipper tracking screen still shows the old status;
      3. with the fixed preset, the same read shows the Shipment's true status.
    The presenter's observations are asked as yes or no. It ends in the fixed state.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Number = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName

if ($Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-as-found-look-alike.ps1 <Transport Order number>, such as TO-0000042' }
if ([Console]::IsInputRedirected) { Write-Fail "the Shipper's and the Carrier's sides are the presenter's, so run this in a terminal" }

$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
$apiManagement = Get-RequiredEnvironmentVariable AZURE_ESTATE_API_MANAGEMENT 'Deploy the estate first: azd hooks run preprovision'

function Get-TrackingPolicy {
    (Invoke-Az rest --method get --output tsv --query properties.value `
            --url "https://management.azure.com/subscriptions/$($env:AZURE_SUBSCRIPTION_ID)/resourceGroups/$resourceGroup/providers/Microsoft.ApiManagement/service/$apiManagement/apis/norvia-partner-api/operations/get-shipper-transport-order/policies/policy?api-version=2024-05-01&format=rawxml" |
            Out-String)
}
function Confirm-Observation([string] $Question, [string] $Failure, [string] $Success) {
    if (-not (Read-Confirmation $Question)) { Write-Fail $Failure }
    Write-Pass $Success
}

try {
    Write-Host '1. The look-alike preset'
    Invoke-AsFound look-alike
    if ((Get-TrackingPolicy) -notmatch 'cache-lookup-value') { Write-Fail 'the Shipper tracking policy has no cache under look-alike' }
    Write-Pass 'the Shipper tracking policy caches the answer'
    if ((Get-MetricAlertValue alert-failed-runs-carrier-tracking enabled) -ne 'true') { Write-Fail 'the alerts are not intact under look-alike' }
    Write-Pass 'the alerts are intact: nothing else is broken'

    Write-Host ''
    Write-Host '2. The TMS is right and the Shipper sees an old status'
    Write-Host @"
  1. As the Shipper of Transport Order $Number, open its tracking in the Partner portal ($portal) and note the status.
  2. As the Carrier, send the next Tracking event (it moves the Shipment on). Check the Shipment in the TMS: its status moved.
  3. As the Shipper, read the Transport Order again.
"@
    Read-Host 'Press Enter when you have read it again. ' | Out-Null
    Confirm-Observation 'Did the TMS show the new status?' 'the TMS did not move on, so the scenario has nothing to show' 'the TMS has the new status'
    Confirm-Observation 'Does the Shipper still see the old status?' 'the Shipper saw the new status: the cache is not in the way' 'the Shipper sees the old status (the cache, not the TMS)'

    Write-Host ''
    Write-Host '3. The fixed run'
    Invoke-AsFound fixed
    if ((Get-TrackingPolicy) -match 'cache-lookup-value') { Write-Fail 'the Shipper tracking policy caches under the fixed preset' }
    Write-Pass 'the Shipper tracking policy has no cache'
    Write-Host '  4. As the Shipper, read the Transport Order again.'
    Read-Host 'Press Enter when you have read it. ' | Out-Null
    Confirm-Observation "Does the Shipper see the Shipment's true status?" 'the Shipper still sees the old status' 'the Shipper sees the true status'
    Write-Host 'Done: the estate is in the fixed state.'
}
finally {
    try { Invoke-AsFound fixed | Out-Null }
    catch { Write-Host 'Put the estate back with infra/as-found.ps1 fixed' }
}
