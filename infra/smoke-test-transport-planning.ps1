#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Transport planning (P3.7, not in CI).
.DESCRIPTION
    Book Rotterdam to Düsseldorf as Brouwer Dranken in the Partner portal first, then as the Planner in the TMS UI assign it to
    Van Dijk Transport, reassign it to Kessler Spedition and release it, then run it, signed in to the Azure CLI as the presenter
    the estate was provisioned for, with the Transport Order number the portal showed:

        infra/smoke-test-transport-planning.ps1 <Transport Order number>

    It checks that:
      1. the Transport Order is Planned, with its Shipment;
      2. the Shipment is Released, carried by Kessler Spedition, and its history of assignments shows it was first
         planned to Van Dijk Transport and then reassigned to Kessler Spedition;
      3. it has left both the Planner's list of Transport Orders to plan and their list of Shipments to release;
      4. the assignment, the reassignment and the release each ran on a Message trail of its own, under the Transport
         Order number, publishing ShipmentPlanned, ShipmentReassigned and ShipmentReleased on the Shipment events
         topic in turn.
    The TMS's queries answer only a principal holding one of its roles, so a throwaway Container Apps job reads them as
    the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a
    body, as the Transport Order holds the Consignee's personal data. It ends with the TMS UI's and the Integration
    console's pages to open. The presenter's steps are in docs/processes/tms/p3-transport-planning.md.
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

if ($Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-transport-planning.ps1 <Transport Order number>, such as TO-0000042' }

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
Write-Host "TMS: $tms"
Write-Host "Transport Order: $Number"

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
        "carrierName": None if carrier is None else carrier["name"],
    }

number = os.environ["NUMBER"]
order = get("/v1/transport-orders/" + number)
# Only what the checks need: the Transport Order and the Shipment hold the Consignee's personal data.
result = {"transportOrder": order if "httpStatus" in order else {
    "status": order["status"],
    "shipmentNumber": order["shipmentNumber"],
    "shipperId": order["shipper"]["id"],
}}

shipment_number = result["transportOrder"].get("shipmentNumber")
if shipment_number:
    shipment = get("/v1/shipments/" + shipment_number)
    result["shipment"] = shipment if "httpStatus" in shipment else {
        "status": shipment["status"],
        "shipperId": shipment["shipper"]["id"],
        "correlationId": shipment["correlationId"],
        **carried_by(shipment["carriedBy"]),
        "assignments": [{**carried_by(a["carriedBy"]), "correlationId": a["correlationId"]} for a in shipment["assignments"]],
    }

waiting = get("/v1/transport-orders?status=Accepted")
to_release = get("/v1/shipments/to-release")
result["waitingForPlanning"] = waiting if not isinstance(waiting, list) else any(w["transportOrderNumber"] == number for w in waiting)
result["toRelease"] = to_release if not isinstance(to_release, list) else (
    shipment_number is not None and any(s["shipmentNumber"] == shipment_number for s in to_release))
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
$result = Invoke-ThrowawayJobForResult -Prefix smoke-planning -Calls $calls -IdentityId $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
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

# 1. The Transport Order is Planned, with its Shipment, and Brouwer Dranken's.
$order = $result.transportOrder
if (-not ($order.status -eq 'Planned' -and $order.shipmentNumber -and $order.shipperId -eq $brouwerDrankenId)) {
    Write-Fail "Transport Order $Number isn't Planned for ${brouwerDrankenId}: $($order | ConvertTo-Json -Compress)"
}
$shipmentNumber = $order.shipmentNumber
Write-Pass "Transport Order $Number is Planned, as Shipment $shipmentNumber"

# 2. The Shipment is Released, carried by Kessler Spedition, planned to Van Dijk Transport and reassigned to Kessler.
$shipment = $result.shipment
if (-not ($shipment.status -eq 'Released' -and $shipment.shipperId -eq $brouwerDrankenId -and $shipment.assignedTo -eq 'Carrier' -and $shipment.carrierId -eq $kesslerId)) {
    Write-Fail "Shipment $shipmentNumber isn't Released, carried by Kessler Spedition: $(@{ status = $shipment.status; shipperId = $shipment.shipperId; assignedTo = $shipment.assignedTo; carrierId = $shipment.carrierId } | ConvertTo-Json -Compress)"
}
Write-Pass "Shipment $shipmentNumber is Released, carried by Kessler Spedition"
$assignments = @($shipment.assignments)
if (-not ($assignments.Count -eq 2 -and $assignments[0].carrierId -eq $vanDijkId -and $assignments[1].carrierId -eq $kesslerId)) {
    Write-Fail "Shipment $shipmentNumber's history of assignments isn't Van Dijk Transport then Kessler Spedition: $($assignments | ConvertTo-Json -Compress)"
}
Write-Pass "Shipment $shipmentNumber was planned to Van Dijk Transport and reassigned to Kessler Spedition"

# 3. It has left both the Planner's lists.
if ($result.waitingForPlanning -ne $false) { Write-Fail "Transport Order $Number is still waiting for planning, or the TMS answered $($result.waitingForPlanning | ConvertTo-Json -Compress) for that list" }
if ($result.toRelease -ne $false) { Write-Fail "Shipment $shipmentNumber is still in the Shipments to release, or the TMS answered $($result.toRelease | ConvertTo-Json -Compress) for that list" }
Write-Pass "Transport Order $Number and Shipment $shipmentNumber have left the Planner's lists"

# 4. Each planning step on its own Message trail: the tracking contract's keys (coding guidelines §7), which the TMS
# logs in Properties. The event's publish is a hop of its own, in the outbox worker, continuing the trace of the call
# that raised it (OutboxPublisher) rather than sharing its correlation ID as a tag of its own span, so a trail is
# found by OperationId, as the Integration console does, not by Properties alone (smoke-test-intake.ps1 check 4).
# Azure Monitor names the TMS after its Container Apps environment too: [<environment>]/tms-api.
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
$assignCorrelationId = $shipment.correlationId
$reassignCorrelationId = $assignments[1].correlationId
Assert-Trail assignment $assignCorrelationId ShipmentPlanned
Assert-Trail reassignment $reassignCorrelationId ShipmentReassigned

# The release's correlation ID is its own, and isn't shown by any read: found as the one Succeeded reassign-or-release
# trail under this Transport Order and Shipment that isn't the reassignment's.
$releaseCorrelationId = Wait-Until {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
AppTraces
| where tostring(Properties.transportOrderNumber) == '$Number' and tostring(Properties.flow) == 'transport-planning'
    and tostring(Properties.outcome) == 'Succeeded' and tostring(Properties.shipmentNumber) == '$shipmentNumber'
    and tostring(Properties.correlationId) !in ('$assignCorrelationId', '$reassignCorrelationId') and AppRoleName endswith 'tms-api'
| distinct tostring(Properties.correlationId)
"@
    $rows.Count -gt 0 ? $rows[0][0] : $null
}
if (-not $releaseCorrelationId) { Write-Fail "no Message trail for Transport Order $Number's release, other than its assignment and reassignment, after 10 minutes" }
Assert-Trail release $releaseCorrelationId ShipmentReleased

$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
Write-Host ''
Write-Host 'Open in the TMS UI:'
Write-Host "  Shipment:  $tmsUi/shipments/$shipmentNumber"
Write-Host 'Open in the Integration console, one row per planning step:'
Write-Host "  Search:    $console/?key=$Number"
