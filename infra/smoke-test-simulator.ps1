#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of the Simulator (not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of the Simulator (not in CI). The presenter signs in to the Simulator and runs a small
    simulation, which the script can't do for them: Entra signs them in, and the Simulator's screens are theirs. The script
    stops and says what to do, reads the TMS and Application Insights, and checks. Run it in a terminal, signed in to the
    Azure CLI as the presenter the estate was provisioned for, once the estate's Logic Apps are deployed (azd up does it):

      infra/smoke-test-simulator.ps1 <run number>

    The run number is the one the Simulator's Configure screen says the next run gets; the script asks you to read it before
    you start the run. The presenter's steps (docs/operations/simulator.md, "Presenter smoke run in Azure"): choose Quiet
    Monday, set the seed to 349, start the run and wait until it is Completed. The script then checks that:
      1. the Simulator is up, and Easy Auth stands in front of it: its health check answers, and any other request from a caller
         without a sign-in is sent to Entra;
      2. the Transport Orders the run booked are in the TMS under their Shipper references SIM-<run>-<n>, most of them
         Planned, and some Rejected (the bad bookings Norvia refuses);
      3. the Planner's Shipments are in the TMS: released, in progress or put on Trips;
      4. at least one Trip carries a Stop for one of the run's Transport Orders;
      5. the paths are findable in Application Insights by correlation ID: a booking's correlation ID is found in the
         Simulator, the intake Logic App and the TMS, and a Planner action's correlation ID in the Simulator and the TMS.
    The TMS is read as the intake identity by a throwaway Container Apps job, as the other smoke runs do. It prints numbers and
    identifiers only: never a Consignee, an address or a token.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $RunNumber = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName

if ($RunNumber -notmatch '^[0-9]+$') { Write-Fail 'usage: smoke-test-simulator.ps1 <run number>, such as 1: the number the Configure screen gave the run' }
if ([Console]::IsInputRedirected) { Write-Fail "the presenter's steps are theirs, so run this in a terminal" }

$simulator = Get-AppUrl simulator-web -ResourceGroup $resourceGroup
$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
$intakeLogicApp = $env:AZURE_ESTATE_INTAKE_LOGIC_APP
$referencePrefix = "SIM-$RunNumber-"
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# 1. The Simulator is up, and Easy Auth stands in front of it.
$health = try { (Invoke-WebRequest -Uri "$simulator/health" -SkipHttpErrorCheck).StatusCode } catch { 0 }
if ($health -ne 200) { Write-Fail "the Simulator's health check answered $health, not 200: is $simulator deployed, and is its migration job done?" }
$unsigned = try { Invoke-WebRequest -Uri "$simulator/" -MaximumRedirection 0 -SkipHttpErrorCheck } catch { $null }
$location = $unsigned ? @($unsigned.Headers['Location'])[0] : $null
if (-not ($unsigned.StatusCode -eq 302 -and $location -like 'https://login.microsoftonline.com/*')) {
    Write-Fail "a request to the Simulator without a sign-in should be sent to Entra, and got: $($unsigned.StatusCode)"
}
Write-Pass 'the Simulator is up, and a caller without a sign-in is sent to Entra'

Write-Host @"
As the presenter, in a browser, open the Simulator ($simulator) and sign in with Entra. Someone without the Simulator.Run and
Simulator.Read roles is refused by Entra, so you need both (the estate assigned them to you). Then:
  1. On Configure, check the number the next run gets is $RunNumber.
  2. Choose Quiet Monday, set the seed to 349 and Start run.
  3. Watch the log fill. Wait until the run is Completed (Cancelled or Failed is not a pass).
"@
Read-Host 'Press Enter when the run is Completed. ' | Out-Null

# 2 to 4. The TMS, as the intake identity: the run's Transport Orders, their Shipments and the Trips.
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
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"httpStatus": error.code}

prefix = os.environ["PREFIX"]
orders = {}
for status in ("Accepted", "Planned", "Rejected"):
    listed = get("/v1/transport-orders?status=" + status)
    if isinstance(listed, dict):
        print("SMOKE-RESULT " + json.dumps({"httpStatus": listed["httpStatus"], "reading": "transport orders " + status}))
        raise SystemExit(0)
    orders[status] = [o for o in listed if (o.get("shipperReference") or "").startswith(prefix)]
numbers = {o["transportOrderNumber"] for status in orders.values() for o in status if o.get("transportOrderNumber")}

shipments = 0
for path in ("/v1/shipments/to-release", "/v1/shipments/to-put-on-trips"):
    listed = get(path)
    if isinstance(listed, list):
        shipments += sum(1 for s in listed if s.get("transportOrderNumber") in numbers)

trips_with_run_stops = 0
shipment_numbers_on_trips = set()
trips = get("/v1/trips")
for trip in trips if isinstance(trips, list) else []:
    detail = get("/v1/trips/" + trip["number"])
    stops = detail.get("stops", []) if isinstance(detail, dict) else []
    mine = [s for s in stops if s.get("transportOrderNumber") in numbers]
    if mine:
        trips_with_run_stops += 1
        shipment_numbers_on_trips.update(s["shipmentNumber"] for s in mine)

in_progress = get("/v1/shipments/in-progress")
in_progress_count = len(in_progress) if isinstance(in_progress, list) else 0
print("SMOKE-RESULT " + json.dumps({
    "accepted": len(orders["Accepted"]), "planned": len(orders["Planned"]), "rejected": len(orders["Rejected"]),
    "shipmentsWaiting": shipments, "shipmentsOnTrips": len(shipment_numbers_on_trips), "tripsWithRunStops": trips_with_run_stops,
    "shipmentsInProgress": in_progress_count,
}))
PYTHON
'@
$state = Invoke-ThrowawayJobForResult -Prefix smoke-simulator -Calls $calls -IdentityId $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
    -ResourceGroup $resourceGroup -ReplicaTimeout 600 -Purpose 'reading the TMS' -Environment ([ordered]@{
        TMS               = $tms
        TMS_API_CLIENT_ID = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
        INTAKE_CLIENT_ID  = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
        PREFIX            = $referencePrefix
    })
if ($state.httpStatus) { Write-Fail "the TMS refused reading the run: $($state | ConvertTo-Json -Compress)" }

if (-not ($state.planned -ge 1 -and ($state.planned + $state.accepted) -ge 1)) {
    Write-Fail "no Transport Order of run $RunNumber ($referencePrefix...) is Planned in the TMS: $($state | ConvertTo-Json -Compress)"
}
Write-Pass "run $RunNumber booked $($state.planned + $state.accepted + $state.rejected) Transport Orders in the TMS: $($state.planned) Planned, $($state.accepted) Accepted, $($state.rejected) Rejected"
if (($state.shipmentsWaiting + $state.shipmentsOnTrips + $state.shipmentsInProgress) -lt 1) {
    Write-Fail "none of the run's Planned orders has a Shipment the TMS shows to release, on a Trip or in progress: $($state | ConvertTo-Json -Compress)"
}
Write-Pass "the Planner's Shipments are in the TMS: $($state.shipmentsWaiting) waiting to be released or put on Trips, $($state.shipmentsOnTrips) on Trips"
if ($state.tripsWithRunStops -lt 1) { Write-Fail "no Trip in the TMS carries a Stop for the run's Transport Orders: did the run use a preset that puts Shipments on Trips, and was it Completed?" }
Write-Pass "$($state.tripsWithRunStops) Trip(s) in the TMS carry Stops for the run's Transport Orders"

# 5. The paths in Application Insights, by correlation ID: the ones the Simulator logged for the run (its tracking keys).
function Get-SimulatorCorrelationId([string] $Flow) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
union AppTraces, AppDependencies, AppRequests
| where TimeGenerated > ago(1d) and tostring(Properties.flow) == '$Flow' and tostring(Properties.outcome) == 'Succeeded'
    and isnotempty(tostring(Properties.correlationId))
| order by TimeGenerated desc | take 1 | project tostring(Properties.correlationId)
"@
    $rows.Count -gt 0 ? $rows[0][0] : $null
}
$filter = "tostring(Properties.correlationId) == '{0}' or OperationId == '{0}'"

Write-Host ''
$bookingId = Wait-Until { Get-SimulatorCorrelationId transport-order-intake }
if (-not $bookingId) { Write-Fail 'no booking the Simulator logged as succeeded reached Application Insights in 10 minutes' }
Assert-InApplicationInsights -WorkspaceId $workspace -What "A booking, by correlation ID $bookingId," -Value $bookingId -Roles 'simulator', $intakeLogicApp, 'tms-api' -Filter $filter
$plannerId = Wait-Until { Get-SimulatorCorrelationId transport-planning }
if (-not $plannerId) { Write-Fail 'no Planner action the Simulator logged as succeeded reached Application Insights in 10 minutes' }
Assert-InApplicationInsights -WorkspaceId $workspace -What "A Planner action, by correlation ID $plannerId," -Value $plannerId -Roles 'simulator', 'tms-api' -Filter $filter

Write-Host ''
Write-Host 'Open in the Integration console, one row per hop:'
Write-Host "  A booking:         $console/?key=$bookingId"
Write-Host "  A Planner action:  $console/?key=$plannerId"
Write-Host "Open in the TMS UI, searching for ${referencePrefix}:"
Write-Host "  Transport Orders:  $tmsUi/"
Write-Host "  Trips:             $tmsUi/trips"
