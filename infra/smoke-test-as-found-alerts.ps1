#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the as-found alerts (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set, signed in to the Azure
.DESCRIPTION
    Post-deploy smoke run of the as-found alerts (not in CI), after `azd up` with ESTATE_ALERT_EMAIL set, signed in to the Azure
    CLI as the presenter the estate was provisioned for (docs/operations/as-found.md):

      infra/smoke-test-as-found-alerts.ps1

    It checks that:
      1. with the as-found preset, a sample of the Logic Apps and entities the preset lists has no alert (the rules are absent,
         not disabled), a sample it leaves out still has one, and the action group still has its receiver;
      2. with the fixed preset, every sampled rule exists, is enabled and sends to the action group, which has an email receiver;
      3. with noAlertReceiver=true, the rules exist and the action group has no receiver;
      4. with noInboundLifecycleRule=true, the inbound account has no lifecycle rule (fixed has one), and with carrierSecretInlined=true
         the booking Logic App has the Carrier credential in its settings, and none after fixed. A health check reports where such a
         secret is, never its value, and so does this script;
      5. with staleThresholdsUnset=true, the TMS API's ShipmentStale__ThresholdsSet setting is false (true under fixed), and with
         shipperTrackingCached=true the get-transport-order policy of the Partner API holds the cache (none under fixed).
    It ends in the fixed state. Each step deploys the estate alone, as infra/as-found.ps1 does.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
# Listed by infra/presets/as-found.json: the tracking, booking and billing runs; the tracking and Proof of delivery queues and
# the billing subscription.
$unalerted = 'alert-failed-runs-carrier-tracking', 'alert-failed-runs-carrier-booking', 'alert-failed-runs-billing',
'alert-dead-letter-carrier-tracking', 'alert-dead-letter-carrier-proof-of-delivery', 'alert-dead-letter-billing'
# Not listed: the intake and Trip dispatch runs, the Trip dispatch and Driver reports subscriptions.
$alerted = 'alert-failed-runs-transport-order-intake', 'alert-failed-runs-trip-dispatch',
'alert-dead-letter-trip-dispatch', 'alert-dead-letter-driver-reports'

if (-not $env:ESTATE_ALERT_EMAIL) { Write-Fail 'set the receiver first: azd env set ESTATE_ALERT_EMAIL <address>' }

function Get-ReceiverCount {
    $group = Invoke-Az monitor action-group list --resource-group $resourceGroup --query "[?starts_with(name, 'ag-norvia-')] | [0].emailReceivers | length(@)" --output tsv
    [int] ($group | Out-String).Trim()
}

function Assert-Absent([string] $Name) {
    if ($null -ne (Get-MetricAlertValue $Name name)) { Write-Fail "$Name exists" }
    Write-Pass "$Name is absent"
}
function Assert-Present([string] $Name) {
    $enabled = Get-MetricAlertValue $Name enabled
    if ($null -eq $enabled) { Write-Fail "$Name is missing" }
    if ($enabled -ne 'true') { Write-Fail "$Name is disabled" }
    if ([int] (Get-MetricAlertValue $Name 'length(actions)') -lt 1) { Write-Fail "$Name sends nowhere" }
    Write-Pass "$Name exists, enabled, with an action group"
}

Invoke-AsFound as-found
foreach ($name in $unalerted) { Assert-Absent $name }
foreach ($name in $alerted) { Assert-Present $name }
if ((Get-ReceiverCount) -lt 1) { Write-Fail 'the action group has no receiver under the as-found preset' }
Write-Pass 'the action group still has its receiver'

Invoke-AsFound fixed
foreach ($name in $unalerted + $alerted) { Assert-Present $name }
if ((Get-ReceiverCount) -lt 1) { Write-Fail 'the action group has no receiver under the fixed preset' }
Write-Pass 'the action group has an email receiver'

Invoke-AsFound noAlertReceiver=true
foreach ($name in $unalerted + $alerted) { Assert-Present $name }
if ((Get-ReceiverCount) -ne 0) { Write-Fail 'the action group still has a receiver under noAlertReceiver' }
Write-Pass 'the action group has no receiver, and the alerts exist'

$inboundAccount = (Invoke-Az storage account list --resource-group $resourceGroup --query "[?starts_with(name, 'stinbound')].name | [0]" --output tsv | Out-String).Trim()
$bookingApp = Get-RequiredEnvironmentVariable AZURE_ESTATE_BOOKING_LOGIC_APP 'Deploy the estate first: azd hooks run preprovision'
function Test-LifecycleRule {
    $null -ne (Invoke-Az storage account management-policy show --resource-group $resourceGroup --account-name $inboundAccount --query name --output tsv -AllowFailure)
}
function Get-InlinedSecretCount {
    [int] (Invoke-Az functionapp config appsettings list --resource-group $resourceGroup --name $bookingApp --query "[?name == 'CARRIER_ENDPOINT_KEY'] | length(@)" --output tsv | Out-String).Trim()
}

Invoke-AsFound fixed
if (-not (Test-LifecycleRule)) { Write-Fail 'the inbound account has no lifecycle rule under the fixed preset' }
Write-Pass 'the inbound account has its lifecycle rule'
if ((Get-InlinedSecretCount) -ne 0) { Write-Fail 'the booking Logic App has an inlined Carrier credential under the fixed preset' }
Write-Pass 'the booking Logic App reads the Carrier credentials from Key Vault only'

Invoke-AsFound noInboundLifecycleRule=true
if (Test-LifecycleRule) { Write-Fail 'the inbound account still has a lifecycle rule under noInboundLifecycleRule' }
Write-Pass 'the inbound account has no lifecycle rule'

Invoke-AsFound carrierSecretInlined=true
if ((Get-InlinedSecretCount) -ne 1) { Write-Fail 'the booking Logic App has no inlined Carrier credential under carrierSecretInlined' }
Write-Pass 'the booking Logic App has a Carrier credential in its settings (name only checked, the value is never printed)'

function Get-TmsSetting([string] $Name) {
    (Invoke-Az containerapp show --name tms-api --resource-group $resourceGroup `
            --query "properties.template.containers[0].env[?name == '$Name'].value | [0]" --output tsv | Out-String).Trim()
}
$apiManagement = Get-RequiredEnvironmentVariable AZURE_ESTATE_API_MANAGEMENT 'Deploy the estate first: azd hooks run preprovision'
function Get-TrackingPolicy {
    (Invoke-Az rest --method get --output tsv --query properties.value `
            --url "https://management.azure.com/subscriptions/$($env:AZURE_SUBSCRIPTION_ID)/resourceGroups/$resourceGroup/providers/Microsoft.ApiManagement/service/$apiManagement/apis/norvia-partner-api/operations/get-shipper-transport-order/policies/policy?api-version=2024-05-01&format=rawxml" |
            Out-String)
}

Invoke-AsFound fixed
if ((Get-TmsSetting ShipmentStale__ThresholdsSet) -ne 'true') { Write-Fail 'the Stale thresholds are not set under the fixed preset' }
Write-Pass 'the TMS API has its Stale thresholds'
if ((Get-TrackingPolicy) -match 'cache-lookup-value') { Write-Fail 'the Shipper tracking policy caches under the fixed preset' }
Write-Pass 'the Shipper tracking policy has no cache, and its rate limit'

Invoke-AsFound staleThresholdsUnset=true
if ((Get-TmsSetting ShipmentStale__ThresholdsSet) -ne 'false') { Write-Fail 'the Stale thresholds are still set under staleThresholdsUnset' }
if ((Get-TmsSetting LateDelivery__CheckEnabled) -ne 'true') { Write-Fail 'staleThresholdsUnset changed the Late delivery check' }
Write-Pass 'the Stale thresholds are unset, and only they'

Invoke-AsFound shipperTrackingCached=true
if ((Get-TrackingPolicy) -notmatch 'cache-lookup-value') { Write-Fail 'the Shipper tracking policy has no cache under shipperTrackingCached' }
Write-Pass 'the Shipper tracking policy caches the answer'

function Get-LogLevel([string] $App) {
    (Invoke-Az containerapp show --name $App --resource-group $resourceGroup `
            --query "properties.template.containers[0].env[?name == 'Logging__LogLevel__Default'].value | [0]" --output tsv | Out-String).Trim()
}
$loggingApps = 'tms-api', 'tms-web', 'partner-portal-web', 'driver-app-api', 'driver-app-web', 'erp-web', 'erp-api', 'integration-console-web', 'health-check-web', 'simulator-web'

Invoke-AsFound fixed
foreach ($app in $loggingApps) {
    if ((Get-LogLevel $app) -ne 'Information') { Write-Fail "$app does not log at Information under the fixed preset" }
}
Write-Pass 'every application logs at Information'

Invoke-AsFound chattyTelemetry=true
foreach ($app in $loggingApps) {
    if ((Get-LogLevel $app) -ne 'Debug') { Write-Fail "$app does not log at Debug under chattyTelemetry" }
}
Write-Pass 'every application logs at Debug'

Invoke-AsFound fixed
Write-Host 'Done: the estate is in the fixed state.'
