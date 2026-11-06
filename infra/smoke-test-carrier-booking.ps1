#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Carrier booking (P4.9, not in CI).
.DESCRIPTION
    As the Planner in the TMS UI, release Brouwer Dranken's Rotterdam to Düsseldorf Shipment to Kessler Spedition while it is set
    to Auto-reject, release it again to Van Dijk Transport once it is back in Shipments to release, and confirm it as Van Dijk
    Transport in the Partner portal, then run this, signed in to the Azure CLI as the presenter the estate was provisioned for,
    with the Transport Order number the portal showed:

        infra/smoke-test-carrier-booking.ps1 <Transport Order number>

    It checks that:
      1. the Shipment is Booked, carried by Van Dijk Transport, and has left the Planner's lists of Shipments to release
         and of bookings awaiting an answer;
      2. it has two Carrier bookings, oldest first: Kessler Spedition's Rejected, with its reason code, and Van Dijk
         Transport's Confirmed, with the Carrier booking reference it answered with, each reported sent;
      3. each Carrier booking's release ran on a Message trail, under its Carrier booking number, that published
         ShipmentReleased and had the P4 booking Logic App's report of it sent recorded;
      4. each Carrier booking's answer ran on a Message trail of its own, Succeeded for that Carrier, publishing
         CarrierBookingRejected and CarrierBookingConfirmed in turn on the Shipment events topic.
    The TMS's queries answer only a principal holding one of its roles, so a throwaway Container Apps job reads them as
    the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a
    body, as the Transport Order holds the Consignee's personal data. It ends with the TMS UI's and the Integration
    console's pages to open. The presenter's steps are in docs/processes/tms/p4-carrier-booking.md.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Number = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$brouwerDrankenId = 'SHP-0001'
$vanDijkId = 'CAR-0001'
$kesslerId = 'CAR-0002'

if ($Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-carrier-booking.ps1 <Transport Order number>, such as TO-0000042' }

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
Write-Host "TMS: $tms"
Write-Host "Transport Order: $Number"

# 1 and 2. The TMS's answers, as the intake identity.
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

number = os.environ["NUMBER"]
order = get("/v1/transport-orders/" + number)
# Only what the checks need: the Transport Order and the Shipment hold the Consignee's personal data.
result = {"transportOrder": order if "httpStatus" in order else {
    "shipmentNumber": order["shipmentNumber"],
    "shipperId": order["shipper"]["id"],
}}

shipment_number = result["transportOrder"].get("shipmentNumber")
if shipment_number:
    shipment = get("/v1/shipments/" + shipment_number)
    result["shipment"] = shipment if "httpStatus" in shipment else {
        "status": shipment["status"],
        "shipperId": shipment["shipper"]["id"],
        "assignedTo": shipment["carriedBy"]["assignedTo"],
        "carrierId": (shipment["carriedBy"].get("carrier") or {}).get("id"),
        "carrierBookings": [{
            "number": b["carrierBookingNumber"],
            "carrierId": b["carrier"]["id"],
            "status": b["status"],
            "reference": b["carrierBookingReference"],
            "reasonCode": b["reasonCode"],
            "sent": b["sentAt"] is not None,
        } for b in shipment["carrierBookings"]],
    }

to_release = get("/v1/shipments/to-release")
awaiting = get("/v1/shipments/bookings-awaiting-answer")
result["toRelease"] = to_release if not isinstance(to_release, list) else (
    shipment_number is not None and any(s["shipmentNumber"] == shipment_number for s in to_release))
result["awaitingAnswer"] = awaiting if not isinstance(awaiting, list) else (
    shipment_number is not None and any(b["shipmentNumber"] == shipment_number for b in awaiting))
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
$result = Invoke-ThrowawayJobForResult -Prefix smoke-carrier-booking -Calls $calls -IdentityId $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
    -ResourceGroup $resourceGroup -Purpose 'reading the TMS' -Environment ([ordered]@{
        TMS               = $tms
        TMS_API_CLIENT_ID = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
        INTAKE_CLIENT_ID  = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
        NUMBER            = $Number
    })
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

foreach ($key in 'transportOrder', 'shipment') {
    if (-not $result.$key -or $result.$key.httpStatus) { Write-Fail "the TMS answered $($result.$key.httpStatus ?? 'nothing') for the $key" }
}

# 1. The Shipment is Booked, carried by Van Dijk Transport, and has left both lists.
$shipmentNumber = $result.transportOrder.shipmentNumber
$shipment = $result.shipment
if (-not ($shipment.status -eq 'Booked' -and $shipment.shipperId -eq $brouwerDrankenId -and $shipment.assignedTo -eq 'Carrier' -and $shipment.carrierId -eq $vanDijkId)) {
    Write-Fail "Shipment $shipmentNumber of Transport Order $Number isn't Booked, carried by Van Dijk Transport: $(@{ status = $shipment.status; shipperId = $shipment.shipperId; assignedTo = $shipment.assignedTo; carrierId = $shipment.carrierId } | ConvertTo-Json -Compress)"
}
Write-Pass "Shipment $shipmentNumber of Transport Order $Number is Booked, carried by Van Dijk Transport"
if ($result.toRelease -ne $false) { Write-Fail "Shipment $shipmentNumber is still in the Shipments to release, or the TMS answered $($result.toRelease | ConvertTo-Json -Compress) for that list" }
if ($result.awaitingAnswer -ne $false) { Write-Fail "Shipment $shipmentNumber is still in the bookings awaiting an answer, or the TMS answered $($result.awaitingAnswer | ConvertTo-Json -Compress) for that list" }
Write-Pass "Shipment $shipmentNumber has left the Planner's lists"

# 2. Its two Carrier bookings: Kessler Spedition's rejected, then Van Dijk Transport's confirmed, each reported sent.
$bookings = @($shipment.carrierBookings)
if (-not ($bookings.Count -eq 2 `
            -and $bookings[0].carrierId -eq $kesslerId -and $bookings[0].status -eq 'Rejected' -and $null -ne $bookings[0].reasonCode `
            -and $bookings[1].carrierId -eq $vanDijkId -and $bookings[1].status -eq 'Confirmed' -and $bookings[1].reference)) {
    Write-Fail "Shipment $shipmentNumber's Carrier bookings aren't Kessler Spedition's Rejected, then Van Dijk Transport's Confirmed: $($bookings | ConvertTo-Json -Compress)"
}
$rejectedNumber = $bookings[0].number
$confirmedNumber = $bookings[1].number
Write-Pass "Carrier booking $rejectedNumber was Rejected by Kessler Spedition ($($bookings[0].reasonCode)), $confirmedNumber Confirmed by Van Dijk Transport"
if (@($bookings | Where-Object { -not $_.sent }).Count -gt 0) {
    Write-Fail "A Carrier booking of Shipment $shipmentNumber wasn't reported sent: $(@($bookings | ForEach-Object { @{ number = $_.number; sent = $_.sent } }) | ConvertTo-Json -Compress)"
}
Write-Pass "Carrier bookings $rejectedNumber and $confirmedNumber were both reported sent"

# 3 and 4. Each Carrier booking on its Message trails: the tracking contract's keys (coding guidelines §7), which the
# TMS logs in Properties. A publish is a hop of its own in the outbox worker, continuing the trace of the call that
# raised it (OutboxPublisher), so a trail is found by OperationId, as the Integration console does, not by Properties
# alone (smoke-test-transport-planning.ps1 check 4). Azure Monitor names the TMS after its Container Apps environment
# too: [<environment>]/tms-api.
# Finds the trail of one flow of a Carrier booking and returns its summary. The correlation ID of a Carrier booking's
# trail of one flow: the release's one is found by the report of the booking as sent, which continues it, and the
# answer's is the answer's own, never shown by any read.
function Get-BookingTrail([string] $Booking, [string] $Flow) {
    $correlationId = Wait-Until {
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
AppTraces
| where tostring(Properties.carrierBookingNumber) == '$Booking' and tostring(Properties.flow) == '$Flow'
    and tostring(Properties.outcome) == 'Succeeded' and AppRoleName endswith 'tms-api'
| distinct tostring(Properties.correlationId)
"@
        $rows.Count -gt 0 ? $rows[0][0] : $null
    }
    if (-not $correlationId) { Write-Fail "no Succeeded $Flow trail for Carrier booking $Booking in Application Insights after 10 minutes" }
    $summary = Wait-Until { Get-TrailSummary -WorkspaceId $workspace -CorrelationId $correlationId -Keys shipmentNumber, partyId, carrierBookingNumber }
    if (-not $summary) { Write-Fail "the Message trail $correlationId ($Flow of Carrier booking $Booking) hasn't reached Application Insights after 10 minutes" }
    $summary
}
function Assert-ReleaseTrail([string] $Booking) {
    $summary = Get-BookingTrail $Booking carrier-booking-report
    if (-not ((Test-SameValues $summary.flows 'carrier-booking-report', 'transport-planning') -and (Test-SameValues $summary.outcomes 'Succeeded') `
            -and (Test-SameValues $summary.shipmentNumber $shipmentNumber) -and (Test-SameValues $summary.carrierBookingNumber $Booking) `
            -and $summary.published -contains 'publish ShipmentReleased')) {
        Write-Fail "the release's Message trail doesn't show Carrier booking $Booking's release and sent report Succeeded for Shipment $shipmentNumber, publishing ShipmentReleased: $($summary | ConvertTo-Json -Compress)"
    }
    Write-Pass "Carrier booking $Booking's release published ShipmentReleased and its sent report was recorded, on Message trail $($summary.correlationId)"
}
function Assert-AnswerTrail([string] $Booking, [string] $Carrier, [string] $EventName) {
    $summary = Get-BookingTrail $Booking carrier-booking-answer
    if (-not ((Test-SameValues $summary.flows 'carrier-booking-answer') -and (Test-SameValues $summary.outcomes 'Succeeded') -and (Test-SameValues $summary.shipmentNumber $shipmentNumber) `
            -and (Test-SameValues $summary.partyId $Carrier) -and (Test-SameValues $summary.carrierBookingNumber $Booking) -and (Test-SameValues $summary.published "publish $EventName"))) {
        Write-Fail "the answer's Message trail doesn't show Carrier booking $Booking's answer Succeeded on the carrier-booking-answer flow for Shipment $shipmentNumber, $Carrier, publishing ${Event}: $($summary | ConvertTo-Json -Compress)"
    }
    Write-Pass "Carrier booking $Booking's answer published $EventName on its own Message trail $($summary.correlationId)"
}
Assert-ReleaseTrail $rejectedNumber
Assert-AnswerTrail $rejectedNumber $kesslerId CarrierBookingRejected
Assert-ReleaseTrail $confirmedNumber
Assert-AnswerTrail $confirmedNumber $vanDijkId CarrierBookingConfirmed

$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
Write-Host ''
Write-Host 'Open in the TMS UI:'
Write-Host "  Shipment:  $tmsUi/shipments/$shipmentNumber"
Write-Host 'Open in the Integration console, by the Shipment number and by each Carrier booking number:'
Write-Host "  Shipment:  $console/?key=$shipmentNumber"
Write-Host "  Rejected:  $console/?key=$rejectedNumber"
Write-Host "  Confirmed: $console/?key=$confirmedNumber"
