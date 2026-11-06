#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Trip execution (P12.15, not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of Trip execution (P12.15, not in CI). It runs the demo of the design doc: the Driver's steps
    are the presenter's, in the Driver app, and the Planner's are looked at in the TMS UI; the script stops and says what to do,
    reads the TMS, the Driver app and Application Insights, and checks. Run it in a terminal, signed in to the Azure CLI as the
    presenter the estate was provisioned for, once the Driver reports Logic App is deployed
    (infra/deploy-driver-reports-logic-app.ps1) and the Trip dispatch Logic App has delivered the seeded Trip to the Driver app,
    with the number of the Trip the TMS seeded Dispatched for today (the Trip with the seeded Driver in the TMS UI's Trips):

      infra/smoke-test-trip-execution.ps1 <Trip number>

    The demo (user story 25): the Driver signs in as the seeded Driver's Entra test user and starts the Trip, collects all three
    Shipments, delivers the first with a signature and a photo, reports the second as Delivery failed, delivers the third with
    reservations and ends the Trip. It checks that:
      1. the Trip is Completed in the TMS and the Driver app shows it Completed with every one of the Driver's reports
         Recorded;
      2. the first Shipment is Delivered with its Proof of delivery on the timeline, and its Transport Order is Billable;
      3. the second Shipment is Collected and Awaiting re-delivery, with an open Delivery failed Exception, and is listed in
         Shipments to put on Trips as a Re-delivery;
      4. the third Shipment is Delivered with its Proof of delivery and an open Reservations Exception;
      5. the Trip's reports were processed in order through the session: the Driver reports Logic App recorded them in the
         order the Driver made them, one after the other;
      6. the whole path is findable in Application Insights by correlation ID: the Driver app, the Driver reports Logic App
         and the TMS each logged every correlation ID the Logic App tracked for the Trip, and the Trip number finds the Logic
         App's and the TMS's rows.
    The Trip is read as the intake identity by a throwaway Container Apps job, as the other smoke runs do. It prints only what
    the checks need: never a signature, a photo, a remark or a token. The presenter's steps are in
    docs/processes/tms/p12-trip-execution.md.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $TripNumber = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName

if ($TripNumber -notmatch '^TR-[0-9]+$') { Write-Fail 'usage: smoke-test-trip-execution.ps1 <Trip number>, such as TR-1000001' }
if ([Console]::IsInputRedirected) { Write-Fail "the Driver's actions are the presenter's, so run this in a terminal" }

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$driverApp = Get-AppUrl driver-app-api -ResourceGroup $resourceGroup
$driverAppUi = Get-AppUrl driver-app-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
$logicApp = $env:AZURE_ESTATE_DRIVER_REPORTS_LOGIC_APP
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# The Trip and its Shipments as the TMS has them, read as the intake identity by a throwaway job.
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

detail = get("/v1/trips/" + os.environ["TRIP"])
if "httpStatus" in detail:
    print("SMOKE-RESULT " + json.dumps(detail))
    raise SystemExit(0)
open_exceptions = get("/v1/exceptions/open")
to_put_on_trips = get("/v1/shipments/to-put-on-trips")
result = {
    "status": detail["trip"]["status"],
    "driverId": detail["trip"]["driverId"],
    "stops": [{"kind": s["kind"], "shipmentNumber": s["shipmentNumber"], "outcome": s["outcome"]} for s in detail["stops"]],
    "shipments": {},
}
# The Shipments the Trip carried: Stops go when a Shipment is taken off at the end, so the Trip number is read from the Stops
# the Trip kept and from the Shipments the Driver app listed, which the checks name.
for number in os.environ["SHIPMENTS"].split():
    shipment = get("/v1/shipments/" + number)
    timeline = get("/v1/shipments/" + number + "/tracking-events")
    result["shipments"][number] = {
        "status": shipment.get("status"),
        "awaitingRedelivery": shipment.get("awaitingRedelivery"),
        "events": [e["type"] for e in timeline.get("trackingEvents", [])],
        "proofsOfDelivery": len(timeline.get("proofsOfDelivery", [])),
        "billingState": (timeline.get("billing") or {}).get("billingState"),
        "openExceptions": [x["type"] for x in open_exceptions if x["shipmentNumber"] == number] if isinstance(open_exceptions, list) else None,
        "listedAsRedelivery": any(s["shipmentNumber"] == number and s["redelivery"] for s in to_put_on_trips) if isinstance(to_put_on_trips, list) else None,
    }
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
$job = Get-ThrowawayJobName 'smoke-trip-execution'
try {
    # Creates the job for the Shipments given, starts it and returns what it read.
    function Read-Tms([string] $Shipments) {
        $definition = Get-ThrowawayJobDefinition -Name smoke-trip-execution -Calls $calls -IdentityId $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
            -ResourceGroup $resourceGroup -Environment ([ordered]@{
                TMS               = $tms
                TMS_API_CLIENT_ID = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
                INTAKE_CLIENT_ID  = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
                TRIP              = $TripNumber
                SHIPMENTS         = $Shipments
            })
        $startedAt = [DateTime]::UtcNow.AddMinutes(-1)
        New-ThrowawayJob -Name $job -Definition $definition -ResourceGroup $resourceGroup | Out-Null
        $started = Start-ThrowawayJob -Name $job -ResourceGroup $resourceGroup
        if ($started.Status -ne 'Succeeded') { Write-Fail "reading the TMS ended $($started.Status); see the logs of job execution $($started.Execution)" }
        $text = Wait-Until { Get-ThrowawayJobResult -WorkspaceId $workspace -JobName $job -Since $startedAt }
        if (-not $text) { Write-Fail "the job's results didn't reach Log Analytics in 10 minutes" }
        Remove-ThrowawayJob -Name $job -ResourceGroup $resourceGroup
        $text | ConvertFrom-Json
    }

    # The Driver's Trip as the Driver app holds it.
    $seed = Get-Content -Raw (Join-Path $PSScriptRoot '../seed/norvia.seed.json') | ConvertFrom-Json
    $driverId = $seed.demoTripExecution.driverId
    function Get-DriverTrip {
        Invoke-RestMethod -Uri "$driverApp/v1/drivers/$driverId/trips" | Where-Object tripNumber -EQ $TripNumber | Select-Object -First 1
    }

    # The Logic App's rows for the Trip's reports (ADR 0016): when each report was recorded, by its Driver app event ID.
    function Get-RecordedOrder {
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
union AppTraces, AppDependencies, AppRequests
| where tostring(Properties.flow) == 'trip-execution' and tostring(Properties.tripNumber) == '$TripNumber'
    and tostring(Properties.outcome) == 'Recorded' and isnotempty(tostring(Properties.sourceMessageId))
| summarize recordedAt = min(TimeGenerated) by sourceMessageId = tolower(tostring(Properties.sourceMessageId))
| order by recordedAt asc
"@
        $rows.Count -gt 0 ? @($rows | ForEach-Object { $_[0] }) : $null
    }
    # The correlation IDs the Logic App tracked for the Trip: one for each of the Driver's actions.
    function Get-LogicAppCorrelationIds {
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
union AppTraces, AppDependencies, AppRequests
| where tostring(Properties.flow) == 'trip-execution' and tostring(Properties.tripNumber) == '$TripNumber' and isnotempty(tostring(Properties.correlationId))
| distinct tostring(Properties.correlationId)
"@
        $rows.Count -gt 0 ? @($rows | ForEach-Object { $_[0] }) : $null
    }
    $filter = "tostring(Properties.correlationId) == '{0}' or OperationId == '{0}' or tostring(Properties.tripNumber) == '{0}'"

    Write-Host "Trip execution: Trip $TripNumber, Driver $driverId"
    # The Trip must be for today, in Norvia's time: Start Trip is refused on any other day (NotTripDay). The seed puts it on the day the
    # TMS first started (the next Monday on a weekend), so a TMS seeded days ago, or at a weekend, needs a fresh TMS database first.
    $trip = Get-DriverTrip
    if (-not $trip) { Write-Fail "the Driver app doesn't hold Trip $TripNumber for Driver ${driverId}: has the Trip dispatch Logic App delivered it, and is $driverId the Trip's Driver?" }
    $today = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow, 'Europe/Amsterdam').ToString('yyyy-MM-dd')
    $day = $trip.day -is [datetime] ? $trip.day.ToString('yyyy-MM-dd') : "$($trip.day)"
    if ($day -ne $today) { Write-Fail "Trip $TripNumber is for $day, not today ($today), so Start Trip would be refused with NotTripDay; seed a fresh TMS database and run this on a weekday" }
    Write-Host @"
As the Driver in the Driver app ($driverAppUi), signed in as the seeded Driver's Entra test user (the presenter panel
has the sign-in details). Open the Trip of the day, with three Shipments, and:
  1. Start the Trip. As the Planner in the TMS UI ($tmsUi/trips/$TripNumber) see it go In progress.
  2. Collect all three Shipments.
  3. Deliver the first with a signature and a photo. The Planner sees it Delivered with its Proof of delivery on the
     timeline, and its Transport Order Billable.
  4. Report the second as Delivery failed, with a remark. The Planner sees a Delivery failed Exception.
  5. Deliver the third with reservations (and a remark), a signature and no photo. The Planner sees a Reservations Exception.
  6. End the Trip. The second Shipment is in Shipments to put on Trips ($tmsUi/trips) as a Re-delivery.
Wait for every report on the Trip card to show Recorded before pressing Enter.
"@
    Read-Host 'Press Enter when done. ' | Out-Null

    # 1. The Driver app's side: the Trip Completed and every report Recorded.
    $trip = Get-DriverTrip
    if (-not $trip) { Write-Fail "the Driver app doesn't hold Trip $TripNumber for Driver $driverId" }
    $reports = @($trip.reports)
    if (-not ($trip.status -eq 'Completed' -and $reports.Count -gt 0 -and @($reports | Where-Object state -NE 'Recorded').Count -eq 0)) {
        Write-Fail "the Driver app doesn't show Trip $TripNumber Completed with every report Recorded: $(@{ status = $trip.status; reports = @($reports | ForEach-Object { @{ kind = $_.kind; state = $_.state; failureCode = $_.failureCode } }) } | ConvertTo-Json -Compress -Depth 5)"
    }
    $kinds = @($reports.kind)
    if (-not ($kinds[0] -eq 'StartTrip' -and $kinds[-1] -eq 'EndTrip' -and @($kinds | Where-Object { $_ -eq 'Collected' }).Count -eq 3 `
                -and @($kinds | Where-Object { $_ -eq 'Delivered' }).Count -eq 2 -and @($kinds | Where-Object { $_ -eq 'DeliveryFailed' }).Count -eq 1)) {
        Write-Fail "the Driver's reports on Trip $TripNumber aren't the demo's (Start, three Collected, two Delivered, one Delivery failed, End): $($kinds | ConvertTo-Json -Compress)"
    }
    $shipments = @($reports.shipmentNumber | Where-Object { $_ } | Sort-Object -Unique) -join ' '

    $state = Read-Tms $shipments
    if ($state.status -ne 'Completed') { Write-Fail "the TMS doesn't have Trip $TripNumber Completed: $(@{ status = $state.status; httpStatus = $state.httpStatus } | ConvertTo-Json -Compress)" }
    Write-Pass "Trip $TripNumber is Completed in the TMS, and the Driver app shows it Completed with all $($reports.Count) reports Recorded"

    # 2 to 4. The Shipments, by what became of each.
    $byNumber = @($state.shipments.PSObject.Properties)
    $delivered = @($byNumber | Where-Object { $_.Value.status -eq 'Delivered' } | ForEach-Object Name)
    $failed = @($byNumber | Where-Object { $_.Value.awaitingRedelivery -eq $true } | ForEach-Object Name)
    if (-not ($delivered.Count -eq 2 -and $failed.Count -eq 1)) {
        Write-Fail "two Shipments should be Delivered and one Awaiting re-delivery: $(@($byNumber | ForEach-Object { @{ $_.Name = @{ status = $_.Value.status; awaitingRedelivery = $_.Value.awaitingRedelivery } } }) | ConvertTo-Json -Compress -Depth 5)"
    }
    $failedShipment = $failed[0]
    $failedState = $state.shipments.$failedShipment
    if (-not ($failedState.status -eq 'Collected' -and $failedState.openExceptions -contains 'DeliveryFailed' -and $failedState.listedAsRedelivery -eq $true)) {
        Write-Fail "the Shipment that failed ($failedShipment) isn't Collected with an open Delivery failed Exception and listed in Shipments to put on Trips as a Re-delivery: $($failedState | ConvertTo-Json -Compress -Depth 5)"
    }
    Write-Pass "$failedShipment is Collected and Awaiting re-delivery, with a Delivery failed Exception open, and listed in Shipments to put on Trips as a Re-delivery"
    $billable = 0
    $reservations = 0
    foreach ($shipment in $delivered) {
        $s = $state.shipments.$shipment
        if (-not ($s.proofsOfDelivery -ge 1 -and $s.events -contains 'Delivered')) {
            Write-Fail "$shipment isn't Delivered with a Proof of delivery on its timeline: $($s | ConvertTo-Json -Compress -Depth 5)"
        }
        if ($s.billingState -eq 'Billable') { $billable++ }
        if ($s.openExceptions -contains 'Reservations') { $reservations++ }
    }
    if (-not ($billable -ge 1 -and $reservations -eq 1)) {
        Write-Fail "a Delivered Shipment should be Billable (the clean one) and exactly one should have a Reservations Exception, found $billable Billable and $reservations with Reservations: $($state.shipments | ConvertTo-Json -Compress -Depth 5)"
    }
    Write-Pass "the two Delivered Shipments have their Proof of delivery on the timeline; $billable Billable, and one has a Reservations Exception open"

    # 5. In order through the session: the Logic App recorded the reports in the order the Driver made them. The session (keyed by
    # Trip number) is what makes that so, but it is seen only in its result: a report with no 'Recorded' row of the Logic App, or one
    # recorded out of turn, fails this check.
    $expected = @($reports.id | ForEach-Object { "$_".ToLowerInvariant() }) # oldest first
    $actual = Wait-Until { Get-RecordedOrder }
    if (-not $actual) { Write-Fail "no recorded report of Trip $TripNumber reached Application Insights after 10 minutes" }
    if (-not (Test-SameValues $actual $expected)) {
        Write-Fail "the Driver reports Logic App recorded Trip $TripNumber's reports out of order. Made: $($expected -join ' ') Recorded: $(@($actual) -join ' ')"
    }
    Write-Pass "the Logic App recorded Trip $TripNumber's $(@($actual).Count) reports in the order the Driver made them"

    # 6. The whole path, by Trip number and by correlation ID.
    Write-Host ''
    Assert-InApplicationInsights -WorkspaceId $workspace -What "Trip $TripNumber, by its number," -Value $TripNumber -Roles $logicApp, 'tms-api' -Filter $filter
    $ids = Wait-Until { Get-LogicAppCorrelationIds }
    if (-not $ids) { Write-Fail "no correlation ID of Trip $TripNumber reached Application Insights after 10 minutes" }
    foreach ($id in @($ids)) {
        Assert-InApplicationInsights -WorkspaceId $workspace -What "Trip $TripNumber, by correlation ID $id," -Value $id -Roles 'driver-app', $logicApp, 'tms-api' -Filter $filter
    }

    Write-Host ''
    Write-Host 'Open in the TMS UI:'
    Write-Host "  Trip detail:       $tmsUi/trips/$TripNumber"
    Write-Host "  Shipments to put on Trips: $tmsUi/trips"
    Write-Host 'Open in the Integration console, one row per hop:'
    Write-Host "  Search:            $console/?key=$TripNumber"
}
finally {
    Remove-ThrowawayJob -Name $job -ResourceGroup $resourceGroup
}
