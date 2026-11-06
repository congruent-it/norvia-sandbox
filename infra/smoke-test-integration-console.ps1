#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of the Integration console, slice 1 (not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of the Integration console, slice 1 (not in CI). Book a Transport Order as Brouwer
    Dranken in the Partner portal first, then run it, signed in to the Azure CLI as the presenter the estate was
    provisioned for, with the Transport Order number the portal showed and, optionally, Norvia's reference for the
    attempt under it, which is the booking's correlation ID:

      infra/smoke-test-integration-console.ps1 TO-0000042 [<correlation ID>]

    Without a number it takes Brouwer Dranken's latest booking of the last hour. It checks, in the sources the console
    reads, that:
      1. the booking's Message trail is found by its Transport Order number, and by its correlation ID, as one trail of
         the flow transport-order-intake that succeeded;
      2. the trail shows the API Management call, the intake Logic App run and the TMS call, with the same correlation ID;
      3. the intake run's steps open, and the map's and the TMS call's input and output bodies can be fetched.
    It reads as the presenter, not as the console's identity, so the console's own access is seen only on screen. It then
    prints the console's pages to open. It never prints a body: they hold the Consignee's personal data. The presenter's
    steps in the Partner portal and the console are in docs/operations/integration-console.md.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string] $TransportOrderNumber = '',
    [Parameter(Position = 1)][string] $ShownCorrelationId = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$brouwerDrankenId = 'SHP-0001'

# Both go into the queries below, so they must be what they claim to be.
if ($TransportOrderNumber -and $TransportOrderNumber -notmatch '^TO-[0-9]+$') { Write-Fail "$TransportOrderNumber isn't a Transport Order number, such as TO-0000042" }
if ($ShownCorrelationId -and $ShownCorrelationId -notmatch '^[0-9a-fA-F-]{36}$') { Write-Fail "$ShownCorrelationId isn't a correlation ID" }

$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup
function Invoke-Logs([string] $Query) { Invoke-LogAnalyticsQuery -WorkspaceId $workspace $Query }

# The telemetry rows with the tracking contract's keys (coding guidelines §7), read where each source records them, as
# the console reads them: applications straight in Properties, API Management also as its logged X-Correlation-ID
# header, and Logic Apps as tracked properties, a JSON string in Properties.trackedProperties.
$trackedRows = @'
union AppRequests, AppTraces, AppDependencies
| extend tracked = parse_json(tostring(Properties.trackedProperties))
| extend correlationId = coalesce(tostring(Properties.correlationId), tostring(tracked.correlationId),
    tostring(Properties["Response-X-Correlation-ID"]), tostring(Properties["Request-X-Correlation-ID"])),
  flow = coalesce(tostring(Properties.flow), tostring(tracked.flow)),
  outcome = coalesce(tostring(Properties.outcome), tostring(tracked.outcome)),
  transportOrderNumber = coalesce(tostring(Properties.transportOrderNumber), tostring(tracked.transportOrderNumber)),
  partyId = coalesce(tostring(Properties.partyId), tostring(tracked.partyId)),
  runId = tostring(parse_json(tostring(Properties.resource)).runId)
| where isnotempty(correlationId)
'@

if (-not $TransportOrderNumber) {
    $rows = Invoke-Logs @"
$trackedRows
| where TimeGenerated > ago(1h) and AppRoleName == '$($env:AZURE_ESTATE_INTAKE_LOGIC_APP)' and partyId == '$brouwerDrankenId'
    and isnotempty(transportOrderNumber)
| top 1 by TimeGenerated desc | project transportOrderNumber
"@
    $TransportOrderNumber = $rows.Count -gt 0 ? $rows[0][0] : ''
    if (-not $TransportOrderNumber) { Write-Fail "Brouwer Dranken booked nothing in the last hour, or it isn't in Application Insights yet; pass its Transport Order number" }
}
Write-Host "Transport Order: $TransportOrderNumber"

# 1. Found by the Transport Order number, and by the correlation ID, as one trail.
$correlationIds = Wait-Until {
    $rows = Invoke-Logs @"
$trackedRows | where Properties has '$TransportOrderNumber'
| where correlationId =~ '$TransportOrderNumber' or transportOrderNumber =~ '$TransportOrderNumber'
| summarize by correlationId
"@
    $rows.Count -gt 0 ? @($rows | ForEach-Object { $_[0] }) : $null
}
if (-not $correlationIds) { Write-Fail "no Message trail carries Transport Order $TransportOrderNumber after 10 minutes" }
if (@($correlationIds).Count -ne 1) { Write-Fail "Transport Order $TransportOrderNumber is on more than one trail: $(@($correlationIds) -join ' ')" }
$correlationId = @($correlationIds)[0]
Write-Pass "the Message trail is found by Transport Order $TransportOrderNumber (correlation ID $correlationId)"
if ($ShownCorrelationId -and $ShownCorrelationId -ne $correlationId) {
    Write-Fail "the trail's correlation ID is $correlationId, not $ShownCorrelationId, the reference the Partner portal showed"
}

# As the console's search sums up a trail: its business keys, flows and outcomes.
$rows = Invoke-Logs @"
$trackedRows | where Properties has '$correlationId' and correlationId =~ '$correlationId'
| summarize transportOrderNumbers = make_set_if(transportOrderNumber, isnotempty(transportOrderNumber)),
    flows = make_set_if(flow, isnotempty(flow)), outcomes = make_set_if(outcome, isnotempty(outcome))
"@
$trail = $rows.Count -gt 0 ? [ordered]@{
    transportOrderNumbers = @($rows[0][0] | ConvertFrom-Json)
    flows                 = @($rows[0][1] | ConvertFrom-Json)
    outcomes              = @($rows[0][2] | ConvertFrom-Json)
} : $null
if (-not ($trail -and $trail.transportOrderNumbers -contains $TransportOrderNumber)) {
    Write-Fail "the trail with correlation ID $correlationId doesn't carry Transport Order $TransportOrderNumber"
}
Write-Pass "the Message trail is found by its correlation ID, and carries Transport Order $TransportOrderNumber"
if (-not ((Test-SameValues $trail.flows 'transport-order-intake') -and (Test-SameValues $trail.outcomes 'Succeeded'))) {
    Write-Fail "the trail isn't one successful transport-order-intake: $($trail | ConvertTo-Json -Compress)"
}
Write-Pass "the trail's flow is transport-order-intake, and it succeeded"

# 2. The API Management call, the intake run and the TMS call, each under the trail's correlation ID. API Management and
# Logic Apps name themselves after their resource; the TMS after its Container Apps environment too: [<environment>]/tms-api.
function Get-Hops {
    $rows = Invoke-Logs @"
$trackedRows | where Properties has '$correlationId' and correlationId =~ '$correlationId'
| where Type == 'AppRequests' or isnotempty(runId)
| summarize runIds = make_set_if(runId, isnotempty(runId)), parties = make_set_if(partyId, isnotempty(partyId)) by AppRoleName
"@
    @($rows | ForEach-Object { [pscustomobject]@{ role = $_[0]; runIds = @($_[1] | ConvertFrom-Json); parties = @($_[2] | ConvertFrom-Json) } })
}
function Select-Hop($Hops, [string] $Role) { @($Hops | Where-Object { $_.role -like "*$Role*" })[0] }
$apim = $env:AZURE_ESTATE_API_MANAGEMENT
$intakeApp = $env:AZURE_ESTATE_INTAKE_LOGIC_APP
$hopsFound = Wait-Until {
    $found = Get-Hops
    ((Select-Hop $found $apim) -and (Select-Hop $found $intakeApp) -and (Select-Hop $found tms-api)) ? , $found : $null
}
# After 10 minutes, say which hop is missing.
if (-not $hopsFound) { $hopsFound = Get-Hops }
if (-not (Select-Hop $hopsFound $apim)) { Write-Fail 'the trail has no API Management call after 10 minutes' }
Write-Pass 'the trail shows the API Management call'
$intake = Select-Hop $hopsFound $intakeApp
if (-not $intake) { Write-Fail 'the trail has no intake Logic App run after 10 minutes' }
if ($intake.runIds.Count -ne 1) { Write-Fail "the trail has $($intake.runIds.Count) intake runs, not one" }
$runId = $intake.runIds[0]
if (-not (Test-SameValues $intake.parties $brouwerDrankenId)) {
    Write-Fail "the intake run tracked the Shipper $($intake.parties | ConvertTo-Json -Compress), not Brouwer Dranken ($brouwerDrankenId)"
}
Write-Pass "the trail shows the intake Logic App run $runId, for Brouwer Dranken ($brouwerDrankenId)"
if (-not (Select-Hop $hopsFound tms-api)) { Write-Fail 'the trail has no TMS call after 10 minutes' }
Write-Pass 'the trail shows the TMS call'

# 3. The run's steps and their bodies, live from the Logic Apps management API, as the console fetches them.
$workflow = 'transport-order-intake'
$runAddress = "https://management.azure.com$($env:AZURE_ESTATE_RESOURCE_GROUP_ID)/providers/Microsoft.Web/sites/$intakeApp/hostruntime/runtime/webhooks/workflow/api/management/workflows/$workflow/runs/$runId"
$steps = Invoke-AzRest -Method get -Url "$runAddress/actions?api-version=2018-11-01"
if (@($steps.value).Count -eq 0) { Write-Fail "run $runId has no steps; has run history passed its retention?" }
Write-Pass "run $runId opens with its steps: $(@($steps.value | ForEach-Object { "$($_.name) $($_.properties.status)" }) -join ', ')"
function Test-BodyFetched([string] $Step, [string] $Link) {
    $uri = ($steps.value | Where-Object name -EQ $Step).properties.$Link.uri
    $uri -and (Invoke-WebRequest -Uri $uri -SkipHttpErrorCheck).StatusCode -eq 200
}
foreach ($step in 'Map_the_booking', 'Submit_to_the_TMS') {
    if (-not (Test-BodyFetched $step inputsLink)) { Write-Fail "$step's input body can't be fetched" }
    if (-not (Test-BodyFetched $step outputsLink)) { Write-Fail "$step's output body can't be fetched" }
    Write-Pass "$step opens with its input and output bodies"
}

$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
Write-Host ''
Write-Host 'Open in the Integration console:'
Write-Host "  search:  $console/?key=$TransportOrderNumber"
Write-Host "  trail:   $console/trails/$correlationId"
Write-Host "  run:     $console/runs/$intakeApp/$workflow/$runId"
