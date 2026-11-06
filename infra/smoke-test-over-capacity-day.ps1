#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of the Over capacity day (P10.8, not in CI): user story 14.
.DESCRIPTION
    Post-deploy presenter smoke run of the Over capacity day (P10.8, not in CI): user story 14. The TMS seeds one day in
    its first week Over capacity when it first starts (a rigid truck Off the road, a CE Driver on leave, and four Own fleet
    Transport Orders, 80 pallet places against 77). Signed in to the Azure CLI as the presenter the estate was
    provisioned for, run it once without arguments to see that day:

      infra/smoke-test-over-capacity-day.ps1

    Then book one more Transport Order from Rotterdam as Brouwer Dranken in the Partner portal, collected that day,
    assign it to Own fleet as the Planner in the TMS UI (the Over capacity warning shows, and the assignment goes ahead),
    reassign it to Kessler Spedition, and run it again with the Transport Order number the portal showed:

      infra/smoke-test-over-capacity-day.ps1 <Transport Order number>

    It checks that:
      1. the seeded day is Over capacity in the Capacity view, with a Vehicle fewer than the Own fleet has and a Driver
         fewer at work (without a number, this is all it checks, and it prints the day to book for);
      2. the Transport Order is Planned for a Shipment carried by Kessler Spedition, collecting on that day, whose history
         of assignments shows Own fleet first and then Kessler Spedition;
      3. the day is still Over capacity with the Shipment gone, and the Shipment's collection day is one the Capacity view
         shows Over capacity;
      4. the assignment and the reassignment each ran on a Message trail of their own, under the Transport Order number,
         publishing ShipmentPlanned and ShipmentReassigned on the Shipment events topic in turn.
    The warning itself is shown only to the Planner, in the TMS UI, when the assignment is made; nothing keeps it, so the
    presenter sees it on screen. The TMS's queries answer only a principal holding one of its roles, so a throwaway
    Container Apps job reads them as the intake identity. It prints only what the checks need, which this script reads
    from Log Analytics: never a body, as the Transport Order holds the Consignee's personal data. It ends with the TMS
    UI's and the Integration console's pages to open. The presenter's steps are in
    docs/processes/tms/p10-fleet-capacity-planning.md.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Number = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$brouwerDrankenId = 'SHP-0001'
$kesslerId = 'CAR-0002'

if ($Number -and $Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-over-capacity-day.ps1 [<Transport Order number>], such as TO-0000042' }

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
Write-Host "TMS: $tms"
if ($Number) { Write-Host "Transport Order: $Number" }

# 1 to 3. The TMS's answers, as the intake identity.
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

def carried_by(view):
    carrier = view.get("carrier")
    return {
        "assignedTo": view["assignedTo"],
        "carrierId": None if carrier is None else carrier["id"],
    }

days = get("/v1/capacity/days")
result = {"days": days if not isinstance(days, list) else [{
    "date": day["date"],
    "availableVehicles": day["availableVehicles"],
    "drivers": len(day["shifts"]),
    "over": day["load"]["over"],
    "capacityPalletPlaces": day["load"]["capacityPalletPlaces"],
    "demandPalletPlaces": day["load"]["demandPalletPlaces"],
} for day in days]}

number = os.environ["NUMBER"]
if number:
    order = get("/v1/transport-orders/" + number)
    # Only what the checks need: the Transport Order and the Shipment hold the Consignee's personal data.
    result["transportOrder"] = order if "httpStatus" in order else {
        "status": order["status"],
        "shipmentNumber": order["shipmentNumber"],
        "shipperId": order["shipper"]["id"],
        "collectionDate": order["transportOrder"]["collection"]["window"]["earliest"][:10],
    }
    shipment_number = result["transportOrder"].get("shipmentNumber")
    if shipment_number:
        shipment = get("/v1/shipments/" + shipment_number)
        result["shipment"] = shipment if "httpStatus" in shipment else {
            "status": shipment["status"],
            "correlationId": shipment["correlationId"],
            **carried_by(shipment["carriedBy"]),
            "assignments": [{**carried_by(a["carriedBy"]), "correlationId": a["correlationId"]} for a in shipment["assignments"]],
        }
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
$result = Invoke-ThrowawayJobForResult -Prefix smoke-over-capacity -Calls $calls -IdentityId $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
    -ResourceGroup $resourceGroup -Purpose 'reading the TMS' -Environment ([ordered]@{
        TMS               = $tms
        TMS_API_CLIENT_ID = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
        INTAKE_CLIENT_ID  = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
        NUMBER            = $Number
    })
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

if ($result.days -isnot [array]) { Write-Fail "the TMS answered $($result.days.httpStatus ?? 'nothing') for the Capacity view" }

# 1. The seeded day: Over capacity, with a Vehicle fewer than the six of the Own fleet and a Driver fewer than the
# seven at work on a weekday.
$overDays = @($result.days | Where-Object over)
if ($overDays.Count -lt 1) { Write-Fail "no open day is Over capacity; did the TMS seed the demo's Over capacity day? $($result.days | ConvertTo-Json -Compress)" }
$seededDays = @($overDays | Where-Object { $_.availableVehicles -eq 5 -and $_.drivers -eq 6 })
if ($seededDays.Count -lt 1) { Write-Fail "no Over capacity day has five Vehicles and six Drivers at work, as the seeded one does: $($overDays | ConvertTo-Json -Compress)" }
$seededDay = $seededDays[0].date
Write-Pass "$seededDay is Over capacity: $($seededDays[0].demandPalletPlaces) of $($seededDays[0].capacityPalletPlaces) pallet places, with a Vehicle off the road and a Driver on leave"

if (-not $Number) {
    Write-Host ''
    Write-Host "Book one more Transport Order from Rotterdam as Brouwer Dranken, collected on $seededDay, assign it to Own fleet"
    Write-Host 'as the Planner, see the Over capacity warning, reassign it to Kessler Spedition, and run this again with its number.'
    return
}

foreach ($key in 'transportOrder', 'shipment') {
    if (-not $result.$key -or $result.$key.httpStatus) { Write-Fail "the TMS answered $($result.$key.httpStatus ?? 'nothing') for the $key" }
}

# 2. The Transport Order is Planned for Brouwer Dranken, collecting on an Over capacity day, with its Shipment now
# carried by Kessler Spedition after Own fleet.
$order = $result.transportOrder
if (-not ($order.status -eq 'Planned' -and $order.shipmentNumber -and $order.shipperId -eq $brouwerDrankenId)) {
    Write-Fail "Transport Order $Number isn't Planned for ${brouwerDrankenId}: $($order | ConvertTo-Json -Compress)"
}
$shipmentNumber = $order.shipmentNumber
$collectionDate = $order.collectionDate
if ($collectionDate -notin $seededDays.date) {
    Write-Fail "Transport Order $Number collects on $collectionDate, which isn't an Over capacity day ($(@($seededDays.date) | ConvertTo-Json -Compress)); book it for $seededDay"
}
Write-Pass "Transport Order $Number is Planned, as Shipment $shipmentNumber, collecting on the Over capacity day $collectionDate"
$shipment = $result.shipment
if (-not ($shipment.assignedTo -eq 'Carrier' -and $shipment.carrierId -eq $kesslerId)) {
    Write-Fail "Shipment $shipmentNumber isn't carried by Kessler Spedition: $(@{ status = $shipment.status; assignedTo = $shipment.assignedTo; carrierId = $shipment.carrierId } | ConvertTo-Json -Compress)"
}
$assignments = @($shipment.assignments)
if (-not ($assignments.Count -eq 2 -and $assignments[0].assignedTo -eq 'OwnFleet' -and $assignments[1].carrierId -eq $kesslerId)) {
    Write-Fail "Shipment $shipmentNumber's history of assignments isn't Own fleet then Kessler Spedition: $($assignments | ConvertTo-Json -Compress)"
}
Write-Pass "Shipment $shipmentNumber was assigned to Own fleet and reassigned to Kessler Spedition"

# 3. With the Shipment off Own fleet, the day carries what it was seeded with and is still Over capacity: reassigning it
# was the Planner's way out for the one Shipment, not for the seeded day. The Capacity view above was read after the
# reassignment, so $seededDays already counts the Shipment as Kessler Spedition's.
Write-Pass "$collectionDate is still Over capacity with Shipment $shipmentNumber on Kessler Spedition"

# 4. Each planning step on its own Message trail: the tracking contract's keys (coding guidelines §7), which the TMS
# logs in Properties. The event's publish is a hop of its own, in the outbox worker, continuing the trace of the call
# that raised it (OutboxPublisher) rather than sharing its correlation ID as a tag of its own span, so a trail is
# found by OperationId, as the Integration console does, not by Properties alone (smoke-test-intake.ps1 check 4).
# Confirms one Message trail of its own flow, Succeeded for this Transport Order and Shipment, that published the
# one event named.
function Assert-Trail([string] $Action, [string] $CorrelationId, [string] $EventName) {
    $summary = Wait-Until { Get-TrailSummary -WorkspaceId $workspace -CorrelationId $CorrelationId -Keys shipmentNumber, partyId, transportOrderNumber }
    if (-not $summary) { Write-Fail "the Message trail $CorrelationId ($Action of Transport Order $Number) hasn't reached Application Insights after 10 minutes" }
    if (-not ((Test-SameValues $summary.flows 'transport-planning') -and (Test-SameValues $summary.outcomes 'Succeeded') -and (Test-SameValues $summary.shipmentNumber $shipmentNumber) `
            -and (Test-SameValues $summary.partyId $brouwerDrankenId) -and (Test-SameValues $summary.transportOrderNumber $Number) -and (Test-SameValues $summary.published "publish $EventName"))) {
        Write-Fail "the Message trail $CorrelationId doesn't show Transport Order $Number's $Action Succeeded on the transport-planning flow for Shipment $shipmentNumber, $brouwerDrankenId, publishing ${Event}: $($summary | ConvertTo-Json -Compress)"
    }
    Write-Pass "Transport Order $Number's $Action published $EventName on its own Message trail $CorrelationId"
}
Assert-Trail assignment $shipment.correlationId ShipmentPlanned
Assert-Trail reassignment $assignments[1].correlationId ShipmentReassigned

$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
Write-Host ''
Write-Host 'Open in the TMS UI:'
Write-Host "  Capacity:  $tmsUi/capacity/days"
Write-Host "  Shipment:  $tmsUi/shipments/$shipmentNumber"
Write-Host 'Open in the Integration console, one row per planning step:'
Write-Host "  Search:    $console/?key=$Number"
