#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Shipment tracking (P5.11, not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of Shipment tracking (P5.11, not in CI). Book Rotterdam to Düsseldorf as Brouwer Dranken
    and get its Shipment Booked and carried by Van Dijk Transport, as in the presenter smoke run of Carrier booking
    (docs/processes/tms/p4-carrier-booking.md, which leaves Kessler Spedition in Auto-reject; restore it afterwards), then run
    this, signed in to the Azure CLI as the presenter the estate was provisioned for, with its Transport Order number:

      infra/smoke-test-shipment-tracking.ps1 <Transport Order number>

    It checks that:
      1. as Van Dijk Transport, through API Management, PU, DEP and POD each return 202 Accepted, and the Shipment moves
         through Collected, In transit and Delivered, listed in the Planner's Shipments in progress until it is Delivered;
      2. the Shipment's timeline holds the three Tracking events, each of which moved its status;
      3. a Tracking event for a reference no Carrier booking has is accepted, then lands on the tracking queue's dead-letter
         queue with ShipmentNotFound;
      4. each accepted call is findable in Application Insights by its correlation ID, in API Management, the Carrier-tracking
         Logic App and the TMS;
      5. with the TMS unreachable for the Carrier-tracking Logic App, a POD is dead-lettered by Service Bus after the queue's
         maximum delivery count, MaxDeliveryCountExceeded, and, once the TMS is back and the presenter has resubmitted it
         from the Integration console (the dead-letter step the headline incident relies on), the TMS records it.
    A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS's queries answer only a
    principal holding one of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the
    TMS as the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a body,
    as the Transport Order holds the Consignee's personal data, and never a token or a key. The tracking queue's dead-letter
    queue is read with the presenter's own Azure Service Bus Data Receiver right, by locking a message and releasing it again.
    Check 5 stops the TMS by pointing the Logic App's TMS_BASE_URL at an address that answers nothing, and restores it
    afterwards, even when the run fails. The presenter's steps are in docs/processes/tms/p5-shipment-tracking.md.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Number = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$vanDijkId = 'CAR-0001'
$queue = 'carrier-tracking'
$namespace = $env:AZURE_ESTATE_SERVICE_BUS_NAMESPACE

if ($Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-shipment-tracking.ps1 <Transport Order number>, such as TO-0000042' }

$arm = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers"
$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
$vanDijkClientId = Get-PersonaClientId van-dijk-transport
$vanDijkKey = (Invoke-Az rest --method post --url "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/van-dijk-transport/listSecrets?api-version=2024-05-01" `
        --query primaryKey --output tsv | Out-String).Trim()
Write-Host "TMS: $tms"
Write-Host "Transport Order: $Number"

$trackingLogicApp = $env:AZURE_ESTATE_TRACKING_LOGIC_APP
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

function Set-TrackingTmsUrl([string] $Url) {
    Invoke-Az logicapp config appsettings set --subscription $subscription --name $trackingLogicApp --resource-group $resourceGroup `
        --settings "TMS_BASE_URL=$Url" --output none | Out-Null
}

# Runs the Carrier's calls in a throwaway Container Apps job and returns what it saw. $Steps are {name, statusCode, bookingRef
# (null for the Shipment's own), wait (the status to wait for, or null)}.
$calls = @'
set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
PERSONA_TOKEN=$(curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d grant_type=client_credentials -d client_id="$VAN_DIJK_CLIENT_ID" -d scope="$PARTNER_API_CLIENT_ID/.default" \
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

order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
if not order.get("shipmentNumber"):
    print("SMOKE-RESULT " + json.dumps({"shipmentNumber": None}))
    raise SystemExit(0)
shipment_number = order.get("shipmentNumber")
shipment = tms("/v1/shipments/" + shipment_number) if shipment_number else {}
confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
result = {"shipmentNumber": shipment_number, "status": shipment.get("status"), "carrierId": (shipment.get("carriedBy", {}).get("carrier") or {}).get("id"),
          "reference": confirmed[0]["carrierBookingReference"] if confirmed else None, "steps": []}

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2) + timedelta(minutes=int(os.environ["OFFSET_MINUTES"]))
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    reference = step["bookingRef"] or result["reference"]
    event = {"bookingRef": reference, "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["statusCode"],
             "eventTime": (base + timedelta(minutes=10 * index)).isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
    status, headers, _ = call("POST", os.environ["PARTNER_API"] + "/carrier-tracking", {
        "Authorization": "Bearer " + os.environ["PERSONA_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["VAN_DIJK_KEY"],
        "Content-Type": "application/json"}, event)
    seen = {"name": step["name"], "httpStatus": status, "correlationId": headers.get("x-correlation-id")}
    if step["wait"]:
        for _ in range(24):
            now = tms("/v1/shipments/" + shipment_number)
            if now.get("status") == step["wait"]:
                break
            time.sleep(5)
        in_progress = tms("/v1/shipments/in-progress")
        seen["status"] = now.get("status")
        seen["inProgress"] = isinstance(in_progress, list) and any(s["shipmentNumber"] == shipment_number for s in in_progress)
    result["steps"].append(seen)

wanted = int(os.environ["AWAIT_DELIVERED"])
for _ in range(60 if wanted else 0):
    events = tms("/v1/shipments/%s/tracking-events" % shipment_number).get("trackingEvents")
    if isinstance(events, list) and sum(e["type"] == "Delivered" for e in events) >= wanted:
        break
    time.sleep(10)
timeline = tms("/v1/shipments/%s/tracking-events" % shipment_number)
events = timeline.get("trackingEvents")
result["timeline"] = [{"type": e["type"], "moved": e["movedShipment"]} for e in events] if isinstance(events, list) else timeline
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
function Invoke-Steps($Steps, [int] $OffsetMinutes, [int] $AwaitDelivered) {
    Invoke-ThrowawayJobForResult -Prefix smoke-shipment-tracking -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 600 `
        -Purpose "the Carrier's calls" `
        -IdentityId $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID, $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
        -Secrets ([ordered]@{ VAN_DIJK_KEY = $vanDijkKey }) -Environment ([ordered]@{
            TENANT                   = $tenant
            PARTNER_API              = $env:AZURE_ESTATE_PARTNER_API_URL
            PARTNER_API_CLIENT_ID    = $env:AZURE_ESTATE_PARTNER_API_CLIENT_ID
            PARTNER_PORTAL_CLIENT_ID = $env:AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID
            VAN_DIJK_CLIENT_ID       = $vanDijkClientId
            INTAKE_CLIENT_ID         = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
            TMS                      = $tms
            TMS_API_CLIENT_ID        = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
            NUMBER                   = $Number
            RUN                      = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
            OFFSET_MINUTES           = $OffsetMinutes
            AWAIT_DELIVERED          = $AwaitDelivered
            STEPS                    = ConvertTo-Json -InputObject @($Steps) -Compress -Depth 5
        })
}

function Get-StepOf($Result, [string] $Name) { $Result.steps | Where-Object name -EQ $Name | Select-Object -First 1 }

$restoreTms = $false
try {
    # 1 to 3. Van Dijk Transport's tracking, and a Tracking event for a reference that no Carrier booking has.
    $result = Invoke-Steps -OffsetMinutes 0 -AwaitDelivered 0 -Steps @(
        @{ name = 'PU'; statusCode = 'PU'; bookingRef = $null; wait = 'Collected' }
        @{ name = 'DEP'; statusCode = 'DEP'; bookingRef = $null; wait = 'In transit' }
        @{ name = 'POD'; statusCode = 'POD'; bookingRef = $null; wait = 'Delivered' }
        @{ name = 'unknown'; statusCode = 'PU'; bookingRef = 'SMOKE-NO-SUCH-REFERENCE'; wait = $null })
    $shipmentNumber = $result.shipmentNumber
    if (-not $shipmentNumber) { Write-Fail "Transport Order $Number has no Shipment, or the TMS doesn't answer for it" }
    if (-not ($result.carrierId -eq $vanDijkId -and $null -ne $result.reference)) {
        Write-Fail "Shipment $shipmentNumber isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(@{ status = $result.status; carrierId = $result.carrierId; reference = $result.reference } | ConvertTo-Json -Compress)"
    }

    # 1. Accepted, and the Shipment follows. It is listed while it is on its way, and leaves the list when Delivered.
    foreach ($expected in @(@('PU', 'Collected', $true), @('DEP', 'In transit', $true), @('POD', 'Delivered', $false))) {
        $code, $status, $listed = $expected
        $step = Get-StepOf $result $code
        if ($step.httpStatus -ne 202) { Write-Fail "Van Dijk Transport's $code didn't get 202 Accepted: $($step | ConvertTo-Json -Compress)" }
        if (-not ($step.status -eq $status -and $step.inProgress -eq $listed)) {
            Write-Fail "after $code Shipment $shipmentNumber isn't $status, listed in Shipments in progress: ${listed}: $($step | ConvertTo-Json -Compress)"
        }
        Write-Pass "$code returned 202 Accepted and Shipment $shipmentNumber is $status$($listed ? ', in the Shipments in progress' : ', out of them')"
    }

    # 2. The timeline.
    $timeline = @($result.timeline)
    if (-not ((Test-SameValues ($timeline.type) 'Collected', 'Departed', 'Delivered') -and @($timeline | Where-Object { -not $_.moved }).Count -eq 0)) {
        Write-Fail "Shipment $shipmentNumber's timeline isn't Collected, Departed and Delivered, each moving its status: $($timeline | ConvertTo-Json -Compress)"
    }
    Write-Pass "Shipment $shipmentNumber's timeline holds Collected, Departed and Delivered, each of which moved its status"

    # 3. The unknown reference is accepted, then dead-lettered with ShipmentNotFound.
    $unknown = Get-StepOf $result unknown
    if ($unknown.httpStatus -ne 202) { Write-Fail "the Tracking event for an unknown reference didn't get 202 Accepted: $($unknown | ConvertTo-Json -Compress)" }
    $unknownId = $unknown.correlationId
    if (-not $unknownId) { Write-Fail "API Management's 202 for the unknown reference carries no correlation ID" }
    Write-Pass "the Tracking event for an unknown reference returned 202 Accepted (correlation ID $unknownId)"
    $reason = Wait-Until { Get-DeadLetterReason -Namespace $namespace -Queue $queue -CorrelationId $unknownId }
    if ($reason -ne 'ShipmentNotFound') { Write-Fail "the unknown reference's message isn't on the $queue dead-letter queue with ShipmentNotFound after 10 minutes: $($reason ? $reason : 'not there')" }
    Write-Pass "the unknown reference's message is on the $queue dead-letter queue, dead-lettered with ShipmentNotFound"

    # 4. Each accepted call by its correlation ID: API Management, the Logic App and, where the TMS was called, the TMS. Azure
    # Monitor names the TMS after its Container Apps environment too: [<environment>]/tms-api.
    $roles = $env:AZURE_ESTATE_API_MANAGEMENT, $trackingLogicApp, 'tms-api'
    foreach ($code in 'PU', 'DEP', 'POD') {
        $correlationId = (Get-StepOf $result $code).correlationId
        Assert-InApplicationInsights -WorkspaceId $workspace -What "$code, by correlation ID $correlationId," -Value $correlationId -Roles $roles
    }
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the unknown reference's Tracking event, by correlation ID $unknownId," -Value $unknownId -Roles $roles

    # 5. With the TMS unreachable, a POD is retried from the queue and dead-lettered by Service Bus after the maximum delivery
    # count. The Shipment is Delivered, so the TMS only logs the POD again once it is resubmitted.
    Write-Host 'Pointing the Carrier-tracking Logic App away from the TMS...'
    $restoreTms = $true
    Set-TrackingTmsUrl 'https://tms-api.stopped.invalid'
    Start-Sleep -Seconds 60
    $outage = Invoke-Steps -OffsetMinutes 60 -AwaitDelivered 0 -Steps @(@{ name = 'POD'; statusCode = 'POD'; bookingRef = $null; wait = $null })
    if ($outage.steps[0].httpStatus -ne 202) { Write-Fail "the POD sent while the TMS was stopped didn't get 202 Accepted: $($outage.steps[0] | ConvertTo-Json -Compress)" }
    $outageId = $outage.steps[0].correlationId
    Write-Pass "a POD sent while the TMS is stopped returns 202 Accepted (correlation ID $outageId)"
    $reason = Wait-Until { Get-DeadLetterReason -Namespace $namespace -Queue $queue -CorrelationId $outageId }
    if ($reason -ne 'MaxDeliveryCountExceeded') { Write-Fail "the POD sent while the TMS was stopped isn't on the $queue dead-letter queue with MaxDeliveryCountExceeded after 10 minutes: $($reason ? $reason : 'not there')" }
    Write-Pass "the POD is on the $queue dead-letter queue, dead-lettered by Service Bus with MaxDeliveryCountExceeded"

    Set-TrackingTmsUrl $tms
    $restoreTms = $false
    $console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
    Write-Host ''
    Write-Host "The TMS is back. Resubmit the POD in the Integration console, from the dead-letter queue of ${queue}:"
    Write-Host "  $console/?key=$outageId"
    if (-not [Console]::IsInputRedirected) {
        Read-Host 'Press Enter once it is resubmitted (the TMS needs the Carrier-tracking Logic App to have restarted first)... ' | Out-Null
        $again = Invoke-Steps -Steps @() -OffsetMinutes 0 -AwaitDelivered 2
        $count = @($again.timeline | Where-Object type -EQ 'Delivered').Count
        if ($count -lt 2) { Write-Fail "the resubmitted POD isn't in Shipment $shipmentNumber's timeline after 10 minutes" }
        Write-Pass "the resubmitted POD was recorded: Shipment $shipmentNumber's timeline holds $count Delivered events"
    }
    else {
        Write-Host "NOT CHECKED: this isn't a terminal, so the resubmission was not waited for. Check Shipment $shipmentNumber's timeline for a second Delivered event."
    }

    $tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
    Write-Host ''
    Write-Host 'Open in the TMS UI:'
    Write-Host "  Shipment:  $tmsUi/shipments/$shipmentNumber"
    Write-Host 'Open in the Integration console, by the Shipment number and by each correlation ID:'
    Write-Host "  Shipment:  $console/?key=$shipmentNumber"
    foreach ($code in 'PU', 'DEP', 'POD') {
        Write-Host "  ${code}: $console/?key=$((Get-StepOf $result $code).correlationId)"
    }
    Write-Host "  Unknown reference: $console/?key=$unknownId"
}
finally {
    # Put back whatever the Logic App was calling, if the run ends while it is pointed away from the TMS.
    if ($restoreTms) {
        try { Set-TrackingTmsUrl $tms }
        catch { Write-Host "Put the Carrier-tracking Logic App's TMS_BASE_URL back to $tms" }
    }
}
