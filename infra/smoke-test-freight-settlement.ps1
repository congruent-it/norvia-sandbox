#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Freight settlement, the TMS side (P9.14, not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of Freight settlement, the TMS side (P9.14, not in CI). It runs user story 14 (release Brouwer
    Dranken's Rotterdam to Düsseldorf Shipment to Van Dijk Transport and see it fail with NoMatchingLane, release it with a Spot
    price, see the price in Van Dijk's inbox, confirm, deliver, send a clean Proof of delivery and see the Transport Order become
    Billable), a PriceNotAccepted from Van Dijk's inbox, and a Delivered order without a Proof of delivery that Customer service
    releases with a remark. The Carriers' calls are the script's; the Planner's and Customer service's actions, and Van Dijk
    Transport's answers in the Partner portal, are the presenter's: the script stops and says what to do. Run it in a terminal,
    signed in to the Azure CLI as the presenter the estate was provisioned for, with three Transport Order numbers:

      infra/smoke-test-freight-settlement.ps1 <priced-by-Spot Transport Order number> <PriceNotAccepted Transport Order number> <no-POD Transport Order number>

    Get the first two assigned to Van Dijk Transport and Planned, but not released: Rotterdam to Düsseldorf as Brouwer Dranken,
    whose Lane Van Dijk's Carrier Rate Card deliberately lacks. Get the third Booked and carried by Kessler Spedition, as in the
    presenter smoke run of Carrier booking (docs/processes/tms/p4-carrier-booking.md), with Kessler Spedition's Auto-progress
    off, as the script sends the Tracking events itself. Do not track any of them first. Keep Van Dijk Transport on its manual answer.
    It checks that:
      1. story 14: the first Shipment is in Shipments to release with the pricing failure NoMatchingLane, and the presenter's
         Release with Spot price leaves a Requested Carrier booking of Van Dijk Transport whose Carrier cost is one Freight line,
         with the source Spot and the remark;
      2. after the presenter's Confirm in Van Dijk's inbox, PU, DEP and POD as Van Dijk Transport return 202 Accepted, the
         Shipment is Delivered, CarrierCostIncurred was published for it, and the order is Not yet billable for NoProofOfDelivery only;
      3. a clean Proof of delivery returns 202 Accepted, the order is Billable and TransportOrderBillable was published;
      4. the second Shipment, released with a Spot price and rejected by the presenter as PriceNotAccepted in Van Dijk's inbox, is
         Planned again, with its Carrier booking Rejected, reason PriceNotAccepted;
      5. the third order, Delivered by Kessler Spedition without a Proof of delivery, is in Customer service's list of Transport
         Orders not yet billable with NoProofOfDelivery only, and after the presenter's release with a remark is Billable, with
         who released it and the remark, and TransportOrderBillable was published;
      6. the whole path is findable in Application Insights by correlation ID: each Tracking event and the Proof of delivery in
         API Management, their Logic Apps and the TMS, the Spot price release in the TMS and the Carrier booking Logic App, and
         the release without a Proof of delivery in the TMS.
    A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS's queries answer only a principal
    holding one of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the TMS as the
    intake identity. It prints only what the checks need: never a body, as the Transport Order holds the Consignee's personal
    data, and never a token or a key. What only a Persona's page shows (Van Dijk's inbox with the price) the script lists at the
    end for the presenter to open. The presenter's steps are in docs/processes/tms/p9-freight-settlement.md.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string] $SpotOrder = '',
    [Parameter(Position = 1)][string] $RejectedOrder = '',
    [Parameter(Position = 2)][string] $NoPodOrder = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$vanDijkId = 'CAR-0001'
$kesslerId = 'CAR-0002'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sampleCmr = Join-Path $repo 'src/PartnerPortal/Norvia.PartnerPortal.Web/CarrierShipments/sample-cmr.pdf'

$orders = $SpotOrder, $RejectedOrder, $NoPodOrder
if (@($orders | Where-Object { $_ -match '^TO-[0-9]+$' }).Count -ne 3 -or @($orders | Select-Object -Unique).Count -ne 3) {
    Write-Fail 'usage: smoke-test-freight-settlement.ps1 <priced-by-Spot Transport Order number> <PriceNotAccepted Transport Order number> <no-POD Transport Order number>, three different ones such as TO-0000042 TO-0000043 TO-0000044'
}
if ([Console]::IsInputRedirected) { Write-Fail "the presenter's actions are the presenter's, so run this in a terminal" }
if (-not (Test-Path $sampleCmr -PathType Leaf)) { Write-Fail "the sample CMR isn't at $sampleCmr" }

$arm = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers"
$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
function Get-PersonaKey([string] $Persona) {
    (Invoke-Az rest --method post --url "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/$Persona/listSecrets?api-version=2024-05-01" `
            --query primaryKey --output tsv | Out-String).Trim()
}
$cmrBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($sampleCmr))
Write-Host "TMS: $tms"
Write-Host "Spot price Transport Order: $SpotOrder"
Write-Host "PriceNotAccepted Transport Order: $RejectedOrder"
Write-Host "No Proof of delivery Transport Order: $NoPodOrder"

$trackingLogicApp = $env:AZURE_ESTATE_TRACKING_LOGIC_APP
$proofLogicApp = $env:AZURE_ESTATE_PROOF_OF_DELIVERY_LOGIC_APP
$bookingLogicApp = $env:AZURE_ESTATE_BOOKING_LOGIC_APP
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# Runs the Carrier's calls in a throwaway Container Apps job, then reads the Transport Order's and Shipment's state, and returns
# what it saw. The steps are {name, kind, ...} where kind is track (statusCode, wait: the Shipment status to wait for, or
# null) or proof (a clean Proof of delivery with the sample CMR).
$calls = @'
set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
PERSONA_TOKEN=$(curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d grant_type=client_credentials -d client_id="$PERSONA_CLIENT_ID" -d scope="$PARTNER_API_CLIENT_ID/.default" \
  -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
  | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])")
export PERSONA_TOKEN
az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
TMS_TOKEN=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
export TMS_TOKEN
python3 - <<"PYTHON"
import json, os, time, urllib.error, urllib.request
from datetime import datetime, timedelta, timezone

def call(method, url, headers, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            text = response.read()
            return response.status, response.headers, (json.loads(text) if text else None)
    except urllib.error.HTTPError as error:
        return error.code, error.headers, None

def tms(path):
    status, _, body = call("GET", os.environ["TMS"] + path, {"Authorization": "Bearer " + os.environ["TMS_TOKEN"]})
    return body if status == 200 else {"httpStatus": status}

def partner(path, body):
    return call("POST", os.environ["PARTNER_API"] + path, {
        "Authorization": "Bearer " + os.environ["PERSONA_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["PERSONA_KEY"],
        "Content-Type": "application/json"}, body)

number = os.environ["NUMBER"]
order = tms("/v1/transport-orders/" + number)
shipment_number = order.get("shipmentNumber")
if not shipment_number:
    print("SMOKE-RESULT " + json.dumps({"shipmentNumber": None}))
    raise SystemExit(0)
shipment = tms("/v1/shipments/" + shipment_number)
confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
result = {"shipmentNumber": shipment_number, "reference": confirmed[0]["carrierBookingReference"] if confirmed else None, "steps": []}

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2) + timedelta(minutes=int(os.environ["OFFSET_MINUTES"]))
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    if step["kind"] == "track":
        event = {"bookingRef": result["reference"], "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["statusCode"],
                 "eventTime": (base + timedelta(minutes=10 * index)).isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
        status, headers, _ = partner("/carrier-tracking", event)
        seen = {"name": step["name"], "httpStatus": status, "correlationId": headers.get("x-correlation-id")}
        if step["wait"]:
            for _ in range(24):
                if tms("/v1/shipments/" + shipment_number).get("status") == step["wait"]:
                    break
                time.sleep(5)
    else:
        document_id = "SMOKE-%s-%s" % (os.environ["RUN"], step["name"])
        proof = {"bookingRef": result["reference"], "documentId": document_id, "receivedBy": "A. Schmitt",
                 "deliveryTime": (base + timedelta(minutes=30)).isoformat(), "place": "Düsseldorf", "postcode": "40591", "country": "DEU",
                 "condition": "CLEAN", "files": [{"name": "sample-cmr.pdf", "contentType": "application/pdf", "content": os.environ["CMR_BASE64"]}]}
        status, headers, _ = partner("/carrier-proofs-of-delivery", proof)
        seen = {"name": step["name"], "httpStatus": status, "correlationId": headers.get("x-correlation-id")}
        for _ in range(36):
            timeline = tms("/v1/shipments/%s/tracking-events" % shipment_number)
            if isinstance(timeline, dict) and any(p["documentId"] == document_id for p in timeline.get("proofsOfDelivery", [])):
                break
            time.sleep(5)
    result["steps"].append(seen)

# The Transport Order and the Shipment as the Planner and Customer service see them now. After a Proof of delivery the order
# is given a minute to become Billable, as the Logic App records it after the call is accepted.
for _ in range(12 if any(s["kind"] == "proof" for s in json.loads(os.environ["STEPS"])) else 0):
    if tms("/v1/transport-orders/" + number).get("billingState") == "Billable":
        break
    time.sleep(5)
order = tms("/v1/transport-orders/" + number)
shipment = tms("/v1/shipments/" + shipment_number)
to_release = tms("/v1/shipments/to-release")
released = [s for s in to_release if s["shipmentNumber"] == shipment_number] if isinstance(to_release, list) else []
not_billable = tms("/v1/transport-orders/not-yet-billable")
unbilled = [o for o in not_billable if o["transportOrderNumber"] == number] if isinstance(not_billable, list) else []
result["status"] = shipment.get("status")
result["carrierId"] = (shipment.get("carriedBy", {}).get("carrier") or {}).get("id")
result["carrierBookings"] = [{"number": b["carrierBookingNumber"], "carrierId": b["carrier"]["id"], "status": b["status"],
                              "reasonCode": b.get("reasonCode"), "carrierCost": b.get("carrierCost")} for b in shipment.get("carrierBookings", [])]
result["pricingFailure"] = (released[0].get("pricingFailure") or {}).get("code") if released else None
result["toReleaseListed"] = bool(released)
result["billingState"] = order.get("billingState")
result["released"] = order.get("proofOfDeliveryRelease") is not None
result["releasedRemark"] = (order.get("proofOfDeliveryRelease") or {}).get("remark")
result["releasedBy"] = (order.get("proofOfDeliveryRelease") or {}).get("releasedBy")
result["notYetBillable"] = {"listed": bool(unbilled), "blockingReasons": unbilled[0]["blockingReasons"] if unbilled else None}
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
function Invoke-Steps($Steps, [string] $Number, [string] $Persona, [int] $OffsetMinutes) {
    Invoke-ThrowawayJobForResult -Prefix smoke-freight-settlement -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 600 `
        -Purpose "the job's calls" `
        -IdentityId $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID, $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
        -Secrets ([ordered]@{ PERSONA_KEY = Get-PersonaKey $Persona }) -Environment ([ordered]@{
            TENANT                   = $tenant
            PARTNER_API              = $env:AZURE_ESTATE_PARTNER_API_URL
            PARTNER_API_CLIENT_ID    = $env:AZURE_ESTATE_PARTNER_API_CLIENT_ID
            PARTNER_PORTAL_CLIENT_ID = $env:AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID
            PERSONA_CLIENT_ID        = Get-PersonaClientId $Persona
            INTAKE_CLIENT_ID         = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
            TMS                      = $tms
            TMS_API_CLIENT_ID        = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
            NUMBER                   = $Number
            RUN                      = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
            OFFSET_MINUTES           = $OffsetMinutes
            CMR_BASE64               = $cmrBase64
            STEPS                    = ConvertTo-Json -InputObject @($Steps) -Compress -Depth 5
        })
}
# The state of a Transport Order without sending anything, for what the presenter just did.
function Get-Look([string] $Number, [string] $Persona) { Invoke-Steps @() $Number $Persona 0 }

# Asks again, for up to three minutes, until $Test holds for the state of Transport Order $Number carried by Persona $Persona.
function Wait-Look([string] $Number, [string] $Persona, [scriptblock] $Test) {
    $look = Get-Look $Number $Persona
    for ($i = 0; $i -lt 18 -and -not (& $Test $look); $i++) {
        Start-Sleep -Seconds 10
        $look = Get-Look $Number $Persona
    }
    $look
}
function Get-Step($Result, [string] $Name) { $Result.steps | Where-Object name -EQ $Name | Select-Object -First 1 }
function Assert-StepAccepted($Result, [string] $Name, [string] $Who) {
    $step = Get-Step $Result $Name
    if ($step.httpStatus -ne 202) { Write-Fail "$Who's $Name didn't get 202 Accepted: $($step | ConvertTo-Json -Compress)" }
}
function Assert-PublishedByCall([string] $What, [string] $CorrelationId, [string] $EventName) {
    if (-not (Wait-Until { Get-PublishedEvent -WorkspaceId $workspace -CorrelationId $CorrelationId -EventName $EventName })) {
        Write-Fail "the Message trail of $What (correlation ID $CorrelationId) didn't publish $EventName in Application Insights after 10 minutes"
    }
}
function Assert-PublishedByAction([string] $What, [string] $Shipment, [string] $Flow, [string] $EventName) {
    if (-not (Wait-Until { Get-PublishedEvent -WorkspaceId $workspace -Shipment $Shipment -Flow $Flow -EventName $EventName })) {
        Write-Fail "no Message trail of $What's $Flow action published $EventName in Application Insights after 10 minutes"
    }
}
function Get-Booking($Result, [scriptblock] $Where) { @(@($Result.carrierBookings) | Where-Object $Where) }

$deliverSteps = @(
    @{ name = 'PU'; kind = 'track'; statusCode = 'PU'; wait = 'Collected' }
    @{ name = 'DEP'; kind = 'track'; statusCode = 'DEP'; wait = 'In transit' }
    @{ name = 'POD'; kind = 'track'; statusCode = 'POD'; wait = 'Delivered' })
$spotBookingOk = { param($r) (Get-Booking $r { $_.carrierId -eq $vanDijkId -and $_.status -eq 'Requested' }).Count -eq 1 }

# Story 14. 1. Releasing to Van Dijk Transport fails on its Carrier Rate Card, and the presenter releases with a Spot price.
Write-Host ''
Write-Host 'Story 14'
$result = Get-Look $SpotOrder van-dijk-transport
$shipmentSpot = $result.shipmentNumber
if (-not $shipmentSpot) { Write-Fail "Transport Order $SpotOrder has no Shipment, or the TMS doesn't answer for it" }
if (-not ($result.status -eq 'Planned' -and $result.carrierId -eq $vanDijkId)) {
    Write-Fail "Shipment $shipmentSpot isn't Planned and assigned to Van Dijk Transport: $(@{ status = $result.status; carrierId = $result.carrierId } | ConvertTo-Json -Compress)"
}
if (-not ($result.toReleaseListed -and $result.pricingFailure -eq 'NoMatchingLane')) {
    Write-Fail "Shipment $shipmentSpot isn't in Shipments to release with the pricing failure NoMatchingLane: $(@{ toReleaseListed = $result.toReleaseListed; pricingFailure = $result.pricingFailure } | ConvertTo-Json -Compress)"
}
Write-Pass "Shipment $shipmentSpot is in Shipments to release with the pricing failure NoMatchingLane"
Wait-ForPresenter "In the TMS UI ($tmsUi), try to release Shipment $shipmentSpot and see it fail with NoMatchingLane. Then choose Release with Spot price,`nenter an amount (such as 480) and the remark `"agreed with Van Dijk by phone`"."
$result = Wait-Look $SpotOrder van-dijk-transport $spotBookingOk
if (-not (& $spotBookingOk $result)) {
    Write-Fail "Shipment $shipmentSpot has no Requested Carrier booking of Van Dijk Transport after the release with a Spot price: $($result.carrierBookings | ConvertTo-Json -Compress -Depth 5)"
}
$requested = (Get-Booking $result { $_.status -eq 'Requested' })[0].carrierCost
if (-not ($requested.currency -eq 'EUR' -and $requested.source.kind -eq 'Spot' -and $requested.source.remark -and $requested.total -gt 0 `
            -and @($requested.chargeLines).Count -eq 1 -and @($requested.chargeLines)[0].type -eq 'Freight')) {
    Write-Fail "the Carrier booking's Carrier cost isn't one Freight line from a Spot price with its remark: $($result.carrierBookings | ConvertTo-Json -Compress -Depth 5)"
}
Write-Pass 'the release with a Spot price left a Requested Carrier booking whose Carrier cost is one Freight line, source Spot, with its remark'

# 2. The presenter confirms in Van Dijk's inbox, and the Shipment is delivered.
Wait-ForPresenter "In the Partner portal, as Van Dijk Transport, open the booking inbox: see the price and that it is a Spot price,`nand Confirm the booking for Shipment $shipmentSpot."
$result = Wait-Look $SpotOrder van-dijk-transport { param($r) $null -ne $r.reference }
if ($null -eq $result.reference) { Write-Fail "Shipment $shipmentSpot's Carrier booking isn't Confirmed after the presenter's Confirm" }
Write-Pass 'Van Dijk Transport confirmed the Spot price: the Carrier booking is Confirmed'
$result = Invoke-Steps $deliverSteps $SpotOrder van-dijk-transport 0
foreach ($code in 'PU', 'DEP', 'POD') { Assert-StepAccepted $result $code 'Van Dijk Transport' }
$puSpotId = (Get-Step $result PU).correlationId
$depSpotId = (Get-Step $result DEP).correlationId
$podSpotId = (Get-Step $result POD).correlationId
if ($result.status -ne 'Delivered') { Write-Fail "Shipment $shipmentSpot isn't Delivered after POD: $($result.status)" }
Write-Pass "PU, DEP and POD returned 202 Accepted and Shipment $shipmentSpot is Delivered (POD's correlation ID $podSpotId)"
Assert-PublishedByCall "Shipment $shipmentSpot's POD" $podSpotId CarrierCostIncurred
Write-Pass "Delivering Shipment $shipmentSpot published CarrierCostIncurred"
$result = Wait-Look $SpotOrder van-dijk-transport { param($r) $r.notYetBillable.listed }
if (-not ($result.billingState -eq 'NotYetBillable' -and $result.notYetBillable.listed -and (Test-SameValues $result.notYetBillable.blockingReasons 'NoProofOfDelivery'))) {
    Write-Fail "Transport Order $SpotOrder isn't Not yet billable for NoProofOfDelivery only: $(@{ billingState = $result.billingState; notYetBillable = $result.notYetBillable } | ConvertTo-Json -Compress -Depth 5)"
}
Write-Pass "Transport Order $SpotOrder is Delivered and Not yet billable, for NoProofOfDelivery only"

# 3. A clean Proof of delivery makes the order Billable.
$result = Invoke-Steps @(@{ name = 'cmr'; kind = 'proof' }) $SpotOrder van-dijk-transport 60
Assert-StepAccepted $result cmr 'Van Dijk Transport'
$cmrSpotId = (Get-Step $result cmr).correlationId
if (-not ($result.billingState -eq 'Billable' -and -not $result.notYetBillable.listed)) {
    Write-Fail "after a clean Proof of delivery Transport Order $SpotOrder isn't Billable and out of Customer service's list: $(@{ billingState = $result.billingState; notYetBillable = $result.notYetBillable } | ConvertTo-Json -Compress -Depth 5)"
}
Assert-PublishedByCall "Transport Order $SpotOrder's Proof of delivery" $cmrSpotId TransportOrderBillable
Write-Pass "a clean Proof of delivery returned 202 Accepted: Transport Order $SpotOrder is Billable and TransportOrderBillable was published (correlation ID $cmrSpotId)"

# Story 14 variant. 4. PriceNotAccepted sends the Shipment back to Planned.
Write-Host ''
Write-Host 'PriceNotAccepted'
$result = Get-Look $RejectedOrder van-dijk-transport
$shipmentRejected = $result.shipmentNumber
if (-not $shipmentRejected) { Write-Fail "Transport Order $RejectedOrder has no Shipment, or the TMS doesn't answer for it" }
if (-not ($result.status -eq 'Planned' -and $result.carrierId -eq $vanDijkId -and $result.pricingFailure -eq 'NoMatchingLane')) {
    Write-Fail "Shipment $shipmentRejected isn't Planned, assigned to Van Dijk Transport, with the pricing failure NoMatchingLane: $(@{ status = $result.status; carrierId = $result.carrierId; pricingFailure = $result.pricingFailure } | ConvertTo-Json -Compress)"
}
Wait-ForPresenter "In the TMS UI, on Shipment ${shipmentRejected}: Release with Spot price, any amount and remark."
$result = Wait-Look $RejectedOrder van-dijk-transport $spotBookingOk
if (-not (& $spotBookingOk $result)) {
    Write-Fail "Shipment $shipmentRejected has no Requested Carrier booking of Van Dijk Transport after the release with a Spot price: $($result.carrierBookings | ConvertTo-Json -Compress -Depth 5)"
}
Wait-ForPresenter 'In the Partner portal, as Van Dijk Transport, reject that booking with the reason PriceNotAccepted.'
$result = Wait-Look $RejectedOrder van-dijk-transport { param($r) $r.status -eq 'Planned' -and (Get-Booking $r { $_.status -eq 'Rejected' }).Count -ge 1 }
if (-not ($result.status -eq 'Planned' -and (Get-Booking $result { $_.status -eq 'Rejected' -and $_.reasonCode -eq 'PriceNotAccepted' }).Count -eq 1)) {
    Write-Fail "Shipment $shipmentRejected isn't Planned again with a Carrier booking Rejected as PriceNotAccepted: $(@{ status = $result.status; carrierBookings = $result.carrierBookings } | ConvertTo-Json -Compress -Depth 5)"
}
Write-Pass "PriceNotAccepted from Van Dijk's inbox sent Shipment $shipmentRejected back to Planned, its Carrier booking Rejected"

# 5. Delivered without a Proof of delivery, released by Customer service with a remark.
Write-Host ''
Write-Host 'Delivered without a Proof of delivery'
$result = Get-Look $NoPodOrder kessler-spedition
if (-not ($result.status -eq 'Booked' -and $result.carrierId -eq $kesslerId -and $null -ne $result.reference)) {
    Write-Fail "Transport Order $NoPodOrder isn't Booked and carried by Kessler Spedition with a Confirmed Carrier booking: $(@{ status = $result.status; carrierId = $result.carrierId; reference = $result.reference } | ConvertTo-Json -Compress)"
}
$result = Invoke-Steps $deliverSteps $NoPodOrder kessler-spedition 0
$shipmentNoPod = $result.shipmentNumber
if (-not $shipmentNoPod) { Write-Fail "Transport Order $NoPodOrder has no Shipment, or the TMS doesn't answer for it" }
foreach ($code in 'PU', 'DEP', 'POD') { Assert-StepAccepted $result $code 'Kessler Spedition' }
$podNoPodId = (Get-Step $result POD).correlationId
$result = Wait-Look $NoPodOrder kessler-spedition { param($r) $r.notYetBillable.listed }
if (-not ($result.status -eq 'Delivered' -and $result.billingState -eq 'NotYetBillable' -and $result.notYetBillable.listed -and (Test-SameValues $result.notYetBillable.blockingReasons 'NoProofOfDelivery'))) {
    Write-Fail "Transport Order $NoPodOrder isn't Delivered and listed as not yet billable for NoProofOfDelivery only: $(@{ status = $result.status; billingState = $result.billingState; notYetBillable = $result.notYetBillable } | ConvertTo-Json -Compress -Depth 5)"
}
Write-Pass "Delivered Transport Order $NoPodOrder shows in Customer service's list with NoProofOfDelivery"
Wait-ForPresenter "In the TMS UI ($tmsUi), in Customer service's list of Transport Orders not yet billable, release $NoPodOrder without a Proof of delivery,`nwith the remark `"CMR received by post, scanned in the dossier`"."
$result = Wait-Look $NoPodOrder kessler-spedition { param($r) $r.billingState -eq 'Billable' }
if (-not ($result.billingState -eq 'Billable' -and $result.released -and $result.releasedBy -and $result.releasedRemark -and -not $result.notYetBillable.listed)) {
    Write-Fail "Transport Order $NoPodOrder isn't Billable, released with its remark, and out of the list: $(@{ billingState = $result.billingState; released = $result.released; notYetBillable = $result.notYetBillable } | ConvertTo-Json -Compress -Depth 5)"
}
Assert-PublishedByAction "Shipment $shipmentNoPod" $shipmentNoPod billing TransportOrderBillable
Write-Pass "Transport Order $NoPodOrder was released with a remark, is Billable and published TransportOrderBillable"
Assert-PublishedByCall "Shipment $shipmentNoPod's POD" $podNoPodId CarrierCostIncurred
Write-Pass "Delivering Shipment $shipmentNoPod published CarrierCostIncurred"

# 6. The whole path, by correlation ID.
Write-Host ''
$trackingRoles = $env:AZURE_ESTATE_API_MANAGEMENT, $trackingLogicApp, 'tms-api'
Assert-InApplicationInsights -WorkspaceId $workspace -What "story 14's PU, by correlation ID $puSpotId," -Value $puSpotId -Roles $trackingRoles
Assert-InApplicationInsights -WorkspaceId $workspace -What "story 14's DEP, by correlation ID $depSpotId," -Value $depSpotId -Roles $trackingRoles
Assert-InApplicationInsights -WorkspaceId $workspace -What "story 14's POD, by correlation ID $podSpotId," -Value $podSpotId -Roles $trackingRoles
Assert-InApplicationInsights -WorkspaceId $workspace -What "story 14's Proof of delivery, by correlation ID $cmrSpotId," -Value $cmrSpotId `
    -Roles $env:AZURE_ESTATE_API_MANAGEMENT, $proofLogicApp, 'tms-api'
Assert-InApplicationInsights -WorkspaceId $workspace -What "the third order's POD, by correlation ID $podNoPodId," -Value $podNoPodId -Roles $trackingRoles
$releaseIds = Wait-Until { Get-ActionIdsReaching -WorkspaceId $workspace -Shipment $shipmentSpot -Flow transport-planning -Role $bookingLogicApp }
if (-not $releaseIds) { Write-Fail "no Planner action on Shipment $shipmentSpot reached the Carrier booking Logic App in Application Insights after 10 minutes" }
$releaseId = @($releaseIds)[0]
Assert-InApplicationInsights -WorkspaceId $workspace -What "the release with a Spot price, by correlation ID $releaseId," -Value $releaseId -Roles $bookingLogicApp, 'tms-api'
$billingIds = Wait-Until { Get-ActionIdsReaching -WorkspaceId $workspace -Shipment $shipmentNoPod -Flow billing -Role tms-api }
if (-not $billingIds) { Write-Fail "no release without a Proof of delivery on Shipment $shipmentNoPod in the TMS in Application Insights after 10 minutes" }
$billingId = @($billingIds)[0]
Assert-InApplicationInsights -WorkspaceId $workspace -What "the release without a Proof of delivery, by correlation ID $billingId," -Value $billingId -Roles tms-api

Write-Host ''
Write-Host "Open in the Partner portal, as Van Dijk Transport (the script can't sign in as the Persona):"
Write-Host "  The booking inbox: the answered request for Shipment $shipmentSpot with its Spot price, and the one for Shipment $shipmentRejected as rejected"
Write-Host 'Open in the TMS UI:'
Write-Host "  Spot Shipment:                $tmsUi/shipments/$shipmentSpot (the Carrier cost, and the billing state, Billable)"
Write-Host "  PriceNotAccepted Shipment:    $tmsUi/shipments/$shipmentRejected"
Write-Host "  Released without a Proof of delivery: $tmsUi/shipments/$shipmentNoPod (the remark and who released it)"
Write-Host 'Open in the Integration console, by the Shipment number and by each correlation ID:'
Write-Host "  Spot Shipment: $console/?key=$shipmentSpot"
Write-Host "  POD:          $console/?key=$podSpotId"
Write-Host "  Proof:        $console/?key=$cmrSpotId"
Write-Host "  No-POD order: $console/?key=$shipmentNoPod"
