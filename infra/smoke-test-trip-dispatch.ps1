#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Trip dispatch (P11.14, not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of Trip dispatch (P11.14, not in CI). Two scenarios. The Planner's actions and the
    presenter panel's switch are the presenter's, in the TMS UI and the Partner portal; the script stops and says what to do,
    reads the TMS, the Driver app and Application Insights, and checks. Run it in a terminal, signed in to the Azure CLI as
    the presenter the estate was provisioned for, once the demo's Shipments are seeded and the Trip dispatch Logic App is
    deployed (infra/deploy-trip-dispatch-logic-app.ps1), with the numbers of the two Trips the presenter builds:

      infra/smoke-test-trip-dispatch.ps1 <scenario 1 Trip number> <scenario 2 Trip number>

    Scenario 1 (the presenter, as the Planner): build a Trip from the seeded Shipments, add the one that is too heavy for the
    van and see PayloadExceeded, dispatch the Trip, wait for Delivered to Driver app, take a Shipment off and wait for version
    2 to be Delivered. Scenario 2: switch "Driver app unavailable" on in the Partner portal's presenter panel, build and
    dispatch a Trip, see Not delivered, switch the fault off and send the version again.
    It checks that:
      1. scenario 1: the Trip is Dispatched with versions 1 and 2, both Delivered to Driver app, and the Driver app holds
         version 2 for the Trip's Driver, with the Stops the TMS has after the take-off;
      2. scenario 2, with the fault on: the Trip's version is Not delivered with a cause, the Driver app does not hold it,
         and the Trip dispatch Logic App's send failed with DriverAppUnavailable;
      3. scenario 2, after the fault is off and the version sent again: the same version is Delivered to Driver app and the
         Driver app holds it, and the fault is off;
      4. the whole path is findable in Application Insights by correlation ID: the TMS, the Trip dispatch Logic App and the
         Driver app each logged the Trip's dispatch, and the Trip number finds the Logic App's rows.
    The TMS's queries answer only a principal holding one of its roles, so a throwaway Container Apps job reads them as the
    intake identity, as the other smoke runs do. It prints only what the checks need. The presenter's steps are in
    docs/processes/tms/p11-trip-dispatch.md.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string] $Scenario1 = '',
    [Parameter(Position = 1)][string] $Scenario2 = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName

if (-not ($Scenario1 -match '^TR-[0-9]+$' -and $Scenario2 -match '^TR-[0-9]+$' -and $Scenario1 -ne $Scenario2)) {
    Write-Fail 'usage: smoke-test-trip-dispatch.ps1 <scenario 1 Trip number> <scenario 2 Trip number>, such as TR-1000001 TR-1000002'
}
if ([Console]::IsInputRedirected) { Write-Fail "the Planner's actions are the presenter's, so run this in a terminal" }

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
$driverApp = Get-AppUrl driver-app-api -ResourceGroup $resourceGroup
$driverAppUi = Get-AppUrl driver-app-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
$logicApp = $env:AZURE_ESTATE_TRIP_DISPATCH_LOGIC_APP
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# The Trips as the TMS has them, read as the intake identity by a throwaway job.
$calls = @'
set -eu
az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
TOKEN=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
export TOKEN
python3 - <<"PYTHON"
import json, os, urllib.error, urllib.request

def get(path):
    request = urllib.request.Request(os.environ["TMS"] + path, headers={"Authorization": "Bearer " + os.environ["TOKEN"]})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"httpStatus": error.code}

result = {}
for number in os.environ["NUMBERS"].split():
    detail = get("/v1/trips/" + number)
    # Only what the checks need: no Stop holds anything of a Consignee here, but the checks need no more than this.
    result[number] = detail if "httpStatus" in detail else {
        "status": detail["trip"]["status"],
        "driverId": detail["trip"]["driverId"],
        "stopCount": len(detail["stops"]),
        "versions": [{"version": v["version"], "state": v["state"], "cause": v["cause"]} for v in detail["versions"]],
    }
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
$job = Get-ThrowawayJobName 'smoke-trip-dispatch'
try {
    $definition = Get-ThrowawayJobDefinition -Name smoke-trip-dispatch -Calls $calls -IdentityId $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
        -ResourceGroup $resourceGroup -Environment ([ordered]@{
            TMS               = $tms
            TMS_API_CLIENT_ID = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
            INTAKE_CLIENT_ID  = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
            NUMBERS           = "$Scenario1 $Scenario2"
        })
    New-ThrowawayJob -Name $job -Definition $definition -ResourceGroup $resourceGroup | Out-Null

    # Starts the job and returns the Trips it read, by Trip number.
    function Read-Trips {
        $startedAt = [DateTime]::UtcNow.AddMinutes(-1)
        $started = Start-ThrowawayJob -Name $job -ResourceGroup $resourceGroup
        if ($started.Status -ne 'Succeeded') { Write-Fail "reading the TMS ended $($started.Status); see the logs of job execution $($started.Execution)" }
        $text = Wait-Until { Get-ThrowawayJobResult -WorkspaceId $workspace -JobName $job -Since $startedAt }
        if (-not $text) { Write-Fail "the job's results didn't reach Log Analytics in 10 minutes" }
        $text | ConvertFrom-Json
    }

    # What the Driver app holds for a Driver, read from its API: the Trip's version, or nothing.
    function Get-DriverAppTrip([string] $Driver, [string] $Trip) {
        Invoke-RestMethod -Uri "$driverApp/v1/drivers/$Driver/trips" | Where-Object tripNumber -EQ $Trip | Select-Object -First 1
    }
    function Get-TripOf($Trips, [string] $Number) { $Trips.$Number }
    function Test-ReadTrip($Trip) { $Trip -and -not $Trip.httpStatus }

    # The send to the Driver app that the Trip dispatch Logic App tracked for a Trip, by the failure code it recorded.
    function Get-SendFailureCode([string] $Trip, [string] $Code) {
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
union AppTraces, AppDependencies, AppRequests
| where tostring(Properties.flow) == 'trip-dispatch' and tostring(Properties.tripNumber) == '$Trip'
    and tostring(Properties.outcome) == 'Failed' and isnotempty(tostring(Properties.failureCode))
| distinct tostring(Properties.failureCode)
"@
        ($rows | Where-Object { $_[0] -eq $Code }) ? 'yes' : $null
    }
    # The correlation IDs the Trip dispatch Logic App tracked for a Trip: each is one Dispatch, Amend or send again.
    function Get-LogicAppCorrelationIds([string] $Trip) {
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
union AppTraces, AppDependencies, AppRequests
| where tostring(Properties.flow) == 'trip-dispatch' and tostring(Properties.tripNumber) == '$Trip' and isnotempty(tostring(Properties.correlationId))
| distinct tostring(Properties.correlationId)
"@
        $rows.Count -gt 0 ? @($rows | ForEach-Object { $_[0] }) : $null
    }
    $correlationFilter = "tostring(Properties.correlationId) == '{0}' or OperationId == '{0}'"

    # Scenario 1.
    Write-Host ''
    Write-Host 'Scenario 1: build, dispatch and amend'
    Write-Host @"
As the Planner in the TMS UI ($tmsUi/trips):
  1. Build Trip $Scenario1 from the seeded Shipments of the demo's day, on the van: select the two that fit a van and the
     one too heavy for it, and see PayloadExceeded when the heavy one is added. Add the two that fit instead.
  2. Pick a Vehicle, a Driver, a planned start and a driving duration, and Dispatch the Trip.
  3. Open the Driver app ($driverAppUi), pick the Trip's Driver, and see the Trip there.
  4. On the Trip's detail take one Shipment off (give a reason) and wait for version 2 to show Delivered to Driver app.
"@
    Read-Host 'Press Enter when done. ' | Out-Null
    $trips = Read-Trips
    $trip = Get-TripOf $trips $Scenario1
    if (-not (Test-ReadTrip $trip)) { Write-Fail "the TMS answered $($trip.httpStatus ?? 'nothing') for Trip $Scenario1" }
    $versions = @($trip.versions)
    if (-not ($trip.status -eq 'Dispatched' -and $versions.Count -eq 2 -and @($versions | Where-Object state -NE 'Delivered').Count -eq 0 `
                -and $versions[0].version -eq 1 -and $versions[1].version -eq 2)) {
        Write-Fail "Trip $Scenario1 isn't Dispatched with versions 1 and 2 Delivered to Driver app: $($trip | ConvertTo-Json -Compress -Depth 5)"
    }
    $driver1 = $trip.driverId
    $held = Get-DriverAppTrip $driver1 $Scenario1
    if ($held.version -ne 2) { Write-Fail "the Driver app doesn't hold version 2 of Trip $Scenario1 for Driver $driver1" }
    Write-Pass "Trip $Scenario1 is Dispatched, versions 1 and 2 are Delivered to Driver app, and the Driver app holds version 2 for Driver $driver1"
    if (@($held.stops).Count -ne $trip.stopCount) { Write-Fail "the Driver app's Stops of Trip $Scenario1 differ from the TMS's $($trip.stopCount)" }
    Write-Pass "the Driver app's version 2 has the same $($trip.stopCount) Stops as the TMS, after the take-off"

    # Scenario 2.
    Write-Host ''
    Write-Host 'Scenario 2: the Driver app unavailable'
    Write-Host @"
  1. Open the presenter panel ($portal/presenter) and switch on the fault driver-app-unavailable. It switches itself off after the
     duration you pick (two minutes by default), so build and dispatch the Trip within that time.
  2. As the Planner, build Trip $Scenario2 from the Shipments that are left (the one that was taken off, and the rest), with
     the other Vehicle and Driver, and Dispatch it.
  3. Wait for the Trip to show Not delivered (the Logic App retries for about a minute first).
"@
    Read-Host 'Press Enter when done. ' | Out-Null
    # The fault switch asks for the Demo.Fault role, which only the portal's identity holds, so the script doesn't read it: the Trip
    # below being Not delivered, and the Driver app not holding it, show the fault was on.
    $trips = Read-Trips
    $trip = Get-TripOf $trips $Scenario2
    if (-not (Test-ReadTrip $trip)) { Write-Fail "the TMS answered $($trip.httpStatus ?? 'nothing') for Trip $Scenario2" }
    $versions = @($trip.versions)
    if (-not ($trip.status -eq 'Dispatched' -and $versions.Count -ge 1 -and $versions[-1].state -eq 'NotDelivered' -and $versions[-1].cause)) {
        Write-Fail "Trip $Scenario2's latest version isn't Not delivered with a cause: $($trip | ConvertTo-Json -Compress -Depth 5)"
    }
    $sentVersion = $versions[-1].version
    $driver2 = $trip.driverId
    if (Get-DriverAppTrip $driver2 $Scenario2) { Write-Fail "the Driver app holds Trip $Scenario2 although it was unavailable" }
    Write-Pass "Trip $Scenario2 version $sentVersion is Not delivered ($($versions[-1].cause)) and the Driver app doesn't hold it"
    if (-not (Wait-Until { Get-SendFailureCode $Scenario2 DriverAppUnavailable })) {
        Write-Fail "no failed send with DriverAppUnavailable for Trip $Scenario2 reached Application Insights after 10 minutes"
    }
    Write-Pass "the Trip dispatch Logic App's send for Trip $Scenario2 failed with DriverAppUnavailable"
    Write-Host ''
    Write-Host "Open the failed run of the $logicApp Logic App in the Integration console: $console/?key=$Scenario2"
    Write-Host @"
  4. Switch the fault off in the presenter panel ($portal/presenter).
  5. On Trip $Scenario2's detail send version $sentVersion again, and wait for it to show Delivered to Driver app.
"@
    Read-Host 'Press Enter when done. ' | Out-Null
    $trips = Read-Trips
    $trip = Get-TripOf $trips $Scenario2
    $versions = @($trip.versions)
    if (-not ($versions.Count -eq $sentVersion -and $versions[-1].version -eq $sentVersion -and $versions[-1].state -eq 'Delivered')) {
        Write-Fail "Trip $Scenario2's version $sentVersion isn't Delivered to Driver app after sending it again: $($trip | ConvertTo-Json -Compress -Depth 5)"
    }
    if ((Get-DriverAppTrip $driver2 $Scenario2).version -ne $sentVersion) { Write-Fail "the Driver app doesn't hold version $sentVersion of Trip $Scenario2 for Driver $driver2" }
    Write-Pass "version $sentVersion of Trip $Scenario2 was sent again and is Delivered to Driver app, so the fault is off"

    # 4. The whole path, by correlation ID and by Trip number.
    Write-Host ''
    foreach ($tripNumber in $Scenario1, $Scenario2) {
        $ids = Wait-Until { Get-LogicAppCorrelationIds $tripNumber }
        if (-not $ids) { Write-Fail "no correlation ID of Trip $tripNumber reached Application Insights after 10 minutes" }
        Assert-InApplicationInsights -WorkspaceId $workspace -What "Trip $tripNumber, by its number," -Value $tripNumber -Roles $logicApp, 'tms-api'
        foreach ($id in @($ids)) {
            Assert-InApplicationInsights -WorkspaceId $workspace -What "Trip $tripNumber, by correlation ID $id," -Value $id `
                -Roles 'tms-api', $logicApp, 'driver-app-api' -Filter $correlationFilter
        }
    }

    Write-Host ''
    Write-Host 'Open in the TMS UI:'
    Write-Host "  Trips:             $tmsUi/trips"
    Write-Host "  Trip detail:       $tmsUi/trips/$Scenario1  and  $tmsUi/trips/$Scenario2"
    Write-Host 'Open in the Integration console, one row per hop:'
    Write-Host "  Search:            $console/?key=$Scenario1  and  $console/?key=$Scenario2"
}
finally {
    Remove-ThrowawayJob -Name $job -ResourceGroup $resourceGroup
}
