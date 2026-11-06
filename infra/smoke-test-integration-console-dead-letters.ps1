#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of the Integration console, slice 2 (not in CI): stop the TMS, find the dead letter,
.DESCRIPTION
    Post-deploy presenter smoke run of the Integration console, slice 2 (not in CI): stop the TMS, find the dead letter,
    resubmit it. Get a Shipment Booked and carried by Van Dijk Transport, as in the presenter smoke run of Carrier booking
    (docs/processes/tms/p4-carrier-booking.md, which leaves Kessler Spedition in Auto-reject; restore it afterwards), then
    run this in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for, with its Transport
    Order number. Do not track it first, and leave the Persona's Auto-progress off: this delivers it.

      infra/smoke-test-integration-console-dead-letters.ps1 <Transport Order number>

    It checks that:
      1. as Van Dijk Transport, through API Management, PU and DEP return 202 Accepted and the Shipment is In transit;
      2. with the TMS container app stopped, a POD returns 202 Accepted, and after the queue's maximum delivery count it is on
         the tracking queue's dead-letter queue, MaxDeliveryCountExceeded, within seconds of its last attempt;
      3. once the TMS is started and the presenter has resubmitted the message in the Integration console, the Shipment is
         Delivered, and its Message trail holds the failed attempts of the Carrier-tracking Logic App and the audit event of
         the resubmit, with the presenter's name;
      4. a Tracking event for a reference no Carrier booking has is dead-lettered with ShipmentNotFound, and, once the
         presenter has discarded it with a reason in the console, it is gone from the dead-letter queue and its Message trail
         holds the audit event of the discard, with the reason.
    It reads as the presenter, and the Carrier's calls are made by a throwaway Container Apps job running as the Partner
    portal, which reads the TMS as the intake identity (ADR 0005). The TMS is stopped with `az containerapp stop`, and started
    again afterwards even when the run fails. It prints only what the checks need: never a body, as the Transport Order holds
    the Consignee's personal data, and never a token or a key. The tracking queue's dead-letter queue is read with the
    presenter's own Azure Service Bus Data Receiver right, by locking a message and releasing it again. The presenter's steps
    are in docs/operations/integration-console.md.
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

if ($Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-integration-console-dead-letters.ps1 <Transport Order number>, such as TO-0000042' }
if ([Console]::IsInputRedirected) { Write-Fail "the resubmit and the discard are the presenter's, in the Integration console, so run this in a terminal" }

$arm = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers"
$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
$vanDijkClientId = Get-PersonaClientId van-dijk-transport
$vanDijkKey = (Invoke-Az rest --method post --url "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/van-dijk-transport/listSecrets?api-version=2024-05-01" `
        --query primaryKey --output tsv | Out-String).Trim()
$trackingLogicApp = $env:AZURE_ESTATE_TRACKING_LOGIC_APP
Write-Host "TMS: $tms"
Write-Host "Transport Order: $Number"

$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# Runs the Carrier's tracking calls in a throwaway Container Apps job and returns what it saw. The steps are {name, statusCode,
# bookingRef (null for the Shipment's own), wait (the status to wait for, or null)}. $BookingReference is the Shipment's Carrier
# booking reference when the TMS is stopped, so the job doesn't read it, and empty when it is up; then the job also reads the
# Shipment's status. $ReadStatus asks for the Shipment's status at the end.
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

reference = os.environ["BOOKING_REFERENCE"]
result = {"steps": []}
if reference:
    shipment_number = os.environ["SHIPMENT_NUMBER"]
else:
    order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
    shipment_number = order.get("shipmentNumber")
    shipment = tms("/v1/shipments/" + shipment_number) if shipment_number else {}
    confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
    reference = confirmed[0]["carrierBookingReference"] if confirmed else None
    result.update(shipmentNumber=shipment_number, status=shipment.get("status"),
                  carrierId=(shipment.get("carriedBy", {}).get("carrier") or {}).get("id"), reference=reference)
    if not shipment_number or shipment.get("status") != "Booked":
        print("SMOKE-RESULT " + json.dumps(result))
        raise SystemExit(0)

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2) + timedelta(minutes=int(os.environ["OFFSET_MINUTES"]))
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    event = {"bookingRef": step["bookingRef"] or reference, "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["statusCode"],
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
        seen["status"] = now.get("status")
    result["steps"].append(seen)

if os.environ["READ_STATUS"] == "1":
    result["status"] = tms("/v1/shipments/" + shipment_number).get("status")
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
$script:bookingReference = ''
$script:shipmentNumber = ''
function Invoke-Steps($Steps, [bool] $ReadStatus, [int] $OffsetMinutes) {
    Invoke-ThrowawayJobForResult -Prefix smoke-console-dead-letters -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 600 `
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
            BOOKING_REFERENCE        = $script:bookingReference
            SHIPMENT_NUMBER          = $script:shipmentNumber
            READ_STATUS              = $ReadStatus ? 1 : 0
            STEPS                    = ConvertTo-Json -InputObject @($Steps) -Compress -Depth 5
        })
}

# The messages on the tracking queue's dead-letter queue, as the presenter: each is locked and released again, never settled.
function Get-Reason([string] $CorrelationId) { Get-DeadLetterReason -Namespace $namespace -Queue $queue -CorrelationId $CorrelationId }

# The audit events the console wrote for a correlation ID, as it reads them for the trail: {action, actor, reason}.
function Get-Audit([string] $CorrelationId, [string] $Action) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
AppTraces
| where Properties has '$CorrelationId'
| where isnotempty(tostring(Properties.Actor)) and isnotempty(tostring(Properties.Action))
| where tostring(Properties.correlationId) =~ '$CorrelationId'
| project action = tostring(Properties.Action), actor = tostring(Properties.Actor), reason = tostring(Properties.Reason)
"@
    $row = $rows | Where-Object { $_[0] -eq $Action } | Select-Object -First 1
    $row ? [pscustomobject]@{ action = $row[0]; actor = $row[1]; reason = $row[2] } : $null
}
# The Carrier-tracking Logic App's runs under a correlation ID: the first attempt and each delivery again.
function Get-AttemptCount([string] $CorrelationId) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
union AppRequests, AppTraces, AppDependencies
| where Properties has '$CorrelationId'
| extend tracked = parse_json(tostring(Properties.trackedProperties))
| where coalesce(tostring(Properties.correlationId), tostring(tracked.correlationId)) =~ '$CorrelationId' and AppRoleName =~ '$trackingLogicApp'
| extend runId = tostring(parse_json(tostring(Properties.resource)).runId)
| where isnotempty(runId)
| summarize runs = dcount(runId)
"@
    $rows.Count -gt 0 ? [int] $rows[0][0] : 0
}

# Start the TMS again, if the run ends while it is stopped.
$tmsStopped = $false
try {
    # 1. Van Dijk Transport collects and departs, so the POD that fails is the one that delivers.
    $result = Invoke-Steps -ReadStatus $false -OffsetMinutes 0 -Steps @(
        @{ name = 'PU'; statusCode = 'PU'; bookingRef = $null; wait = 'Collected' }
        @{ name = 'DEP'; statusCode = 'DEP'; bookingRef = $null; wait = 'In transit' })
    $shipmentNumber = $result.shipmentNumber
    if (-not $shipmentNumber) { Write-Fail "Transport Order $Number has no Shipment, or the TMS doesn't answer for it" }
    if (-not ($result.carrierId -eq $vanDijkId -and $null -ne $result.reference)) {
        Write-Fail "Shipment $shipmentNumber isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(@{ status = $result.status; carrierId = $result.carrierId; reference = $result.reference } | ConvertTo-Json -Compress)"
    }
    if (@($result.steps).Count -eq 0) { Write-Fail "Shipment $shipmentNumber is $($result.status), not Booked: book a new one, as this run delivers it" }
    $script:bookingReference = $result.reference
    $script:shipmentNumber = $shipmentNumber
    foreach ($expected in @(@('PU', 'Collected'), @('DEP', 'In transit'))) {
        $code, $status = $expected
        $step = $result.steps | Where-Object name -EQ $code
        if (-not ($step.httpStatus -eq 202 -and $step.status -eq $status)) {
            Write-Fail "Van Dijk Transport's $code didn't get 202 Accepted and move Shipment $shipmentNumber to ${status}: $($step | ConvertTo-Json -Compress)"
        }
        Write-Pass "$code returned 202 Accepted and Shipment $shipmentNumber is $status"
    }

    # 2. With the TMS stopped, a POD is delivered again until Service Bus dead-letters it.
    Write-Host 'Stopping the TMS...'
    $tmsStopped = $true
    Invoke-Az containerapp stop --name tms-api --resource-group $resourceGroup --output none | Out-Null
    Start-Sleep -Seconds 30
    $outage = Invoke-Steps -ReadStatus $false -OffsetMinutes 60 -Steps @(@{ name = 'POD'; statusCode = 'POD'; bookingRef = $null; wait = $null })
    $outageStep = @($outage.steps)[0]
    if ($outageStep.httpStatus -ne 202) { Write-Fail "the POD sent while the TMS was stopped didn't get 202 Accepted: $($outageStep | ConvertTo-Json -Compress)" }
    $outageId = $outageStep.correlationId
    Write-Pass "a POD sent while the TMS is stopped returns 202 Accepted (correlation ID $outageId)"
    # The queue's lock lasts a minute and it delivers 10 times, so it takes about 10 minutes; once there, the panel shows it on its
    # next refresh and this asks every 5 seconds.
    $reason = Wait-Until -Attempts 360 -IntervalSeconds 5 { Get-Reason $outageId }
    if ($reason -ne 'MaxDeliveryCountExceeded') {
        Write-Fail "the POD sent while the TMS was stopped isn't on the $queue dead-letter queue with MaxDeliveryCountExceeded after 30 minutes: $($reason ? $reason : 'not there')"
    }
    Write-Pass "the POD is on the $queue dead-letter queue, dead-lettered by Service Bus with MaxDeliveryCountExceeded"

    # 3. The TMS is started and the presenter resubmits the message in the console.
    Write-Host 'Starting the TMS...'
    Invoke-Az containerapp start --name tms-api --resource-group $resourceGroup --output none | Out-Null
    $tmsStopped = $false
    for ($i = 0; $i -lt 30; $i++) {
        $health = try { (Invoke-WebRequest -Uri "$tms/health" -SkipHttpErrorCheck).StatusCode } catch { 0 }
        if ($health -eq 200) { break }
        Start-Sleep -Seconds 10
    }
    Write-Host ''
    Write-Host "The TMS is back. Resubmit the POD in the Integration console, on the dead-letter queue of ${queue}:"
    Write-Host "  $console/dead-letter-queues/$queue"
    Write-Host "  trail: $console/trails/$outageId"
    Read-Host 'Press Enter once it is resubmitted... ' | Out-Null
    $delivered = (Invoke-Steps -ReadStatus $true -OffsetMinutes 0 -Steps @()).status
    for ($i = 0; $i -lt 12 -and $delivered -ne 'Delivered'; $i++) {
        Start-Sleep -Seconds 10
        $delivered = (Invoke-Steps -ReadStatus $true -OffsetMinutes 0 -Steps @()).status
    }
    if ($delivered -ne 'Delivered') { Write-Fail "Shipment $shipmentNumber is $delivered, not Delivered, after the resubmit" }
    Write-Pass "after the resubmit Shipment $shipmentNumber is Delivered"
    if (Get-Reason $outageId) { Write-Fail "the resubmitted POD is still on the $queue dead-letter queue" }
    $runs = Wait-Until { $n = Get-AttemptCount $outageId; $n -ge 2 ? $n : $null }
    if (-not $runs) { Write-Fail "the trail $outageId shows fewer than two Carrier-tracking Logic App runs after 10 minutes, so no failed attempts" }
    Write-Pass "the trail shows $runs Carrier-tracking Logic App runs under correlation ID ${outageId}: the failed attempts and the one that delivered"
    $resubmit = Wait-Until { Get-Audit $outageId resubmit }
    if (-not $resubmit) { Write-Fail "the trail $outageId has no audit event of the resubmit after 10 minutes" }
    Write-Pass "the trail holds the audit event of the resubmit, by $($resubmit.actor)"

    # 4. A Tracking event for an unknown reference is dead-lettered with ShipmentNotFound, and the presenter discards it.
    $unknown = Invoke-Steps -ReadStatus $false -OffsetMinutes 0 -Steps @(@{ name = 'unknown'; statusCode = 'PU'; bookingRef = 'SMOKE-NO-SUCH-REFERENCE'; wait = $null })
    $unknownStep = @($unknown.steps)[0]
    if ($unknownStep.httpStatus -ne 202) { Write-Fail "the Tracking event for an unknown reference didn't get 202 Accepted: $($unknownStep | ConvertTo-Json -Compress)" }
    $unknownId = $unknownStep.correlationId
    Write-Pass "the Tracking event for an unknown reference returned 202 Accepted (correlation ID $unknownId)"
    $reason = Wait-Until -Attempts 60 -IntervalSeconds 5 { Get-Reason $unknownId }
    if ($reason -ne 'ShipmentNotFound') {
        Write-Fail "the unknown reference's message isn't on the $queue dead-letter queue with ShipmentNotFound after 5 minutes: $($reason ? $reason : 'not there')"
    }
    Write-Pass "the unknown reference's message is on the $queue dead-letter queue, dead-lettered with ShipmentNotFound"
    Write-Host ''
    Write-Host 'Discard the message in the Integration console, with a reason:'
    Write-Host "  $console/dead-letter-queues/$queue"
    Write-Host "  trail: $console/trails/$unknownId"
    Read-Host 'Press Enter once it is discarded... ' | Out-Null
    if (-not (Wait-Until { (Get-Reason $unknownId) ? $null : 'gone' })) { Write-Fail "the unknown reference's message is still on the $queue dead-letter queue" }
    Write-Pass "the unknown reference's message is gone from the $queue dead-letter queue"
    $discard = Wait-Until { Get-Audit $unknownId discard }
    if (-not $discard) { Write-Fail "the trail $unknownId has no audit event of the discard after 10 minutes" }
    if (-not $discard.reason) { Write-Fail 'the audit event of the discard holds no reason' }
    Write-Pass "the trail holds the audit event of the discard, by $($discard.actor), with its reason"

    Write-Host ''
    Write-Host 'Open in the TMS UI:'
    Write-Host "  Shipment:  $tmsUi/shipments/$shipmentNumber"
    Write-Host 'Open in the Integration console:'
    Write-Host "  Resubmitted POD:  $console/trails/$outageId"
    Write-Host "  Discarded:        $console/trails/$unknownId"
}
finally {
    if ($tmsStopped) {
        try { Invoke-Az containerapp start --name tms-api --resource-group $resourceGroup --output none | Out-Null }
        catch { Write-Host "Start the TMS again: az containerapp start --name tms-api --resource-group $resourceGroup" }
    }
}
