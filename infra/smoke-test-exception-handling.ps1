#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Exception handling (P7.17, not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of Exception handling (P7.17, not in CI). It runs user story 25 (Van Dijk Transport reports
    NDL on Brouwer Dranken's Rotterdam to Düsseldorf Shipment, the Exception opens, Brouwer Dranken is notified, a new window is
    agreed, the Shipment is delivered and the Exception is resolved Redelivered) and user story 26 (a far ETA on a Booked
    Kessler Spedition Shipment, cancel, re-release to Van Dijk Transport). The Carriers' calls are the script's; the Planner's
    actions are the presenter's, in the TMS UI, as in the presenter smoke run of Carrier booking: the script stops and says what
    to do. Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for, with two Transport
    Order numbers:

      infra/smoke-test-exception-handling.ps1 <story 25 Transport Order number> <story 26 Transport Order number>

    Get both Booked first, as in the presenter smoke run of Carrier booking (docs/processes/tms/p4-carrier-booking.md): the first
    carried by Van Dijk Transport, the second by Kessler Spedition (set it to Auto-confirm for it, and leave the Persona's Auto-progress
    off for both, as the script sends the Tracking events itself). Do not track either first.
    It checks that:
      1. story 25: PU, DEP and NDL as Van Dijk Transport return 202 Accepted, and one Delivery failed Exception opens for the
         Shipment, In transit;
      2. the presenter's Notify Shipper ends Sent in the TMS, and the Shipper notification Logic App ran for it;
      3. the presenter's agreed delivery window published ShipmentDeliveryWindowAgreed;
      4. POD delivers the Shipment and its Delivery failed Exception is marked cause cleared, still Open;
      5. the presenter's resolution as Redelivered leaves the list of Open Exceptions and published ExceptionResolved;
      6. story 26: a far ETA, sent by Kessler Spedition on a Booked Shipment, opens a Late delivery Exception, the Shipment still Booked;
      7. the presenter's cancellation leaves Kessler Spedition's Carrier booking Cancelled, the Shipment in Shipments to release
         as Cancelled by Norvia and not flagged Carrier not told, and the Carrier cancellation Logic App ran for it;
      8. with the Shipper notification endpoint stopped (the Logic App pointed away from the Partner portal), a notification the
         presenter requests ends Not delivered after the retries;
      9. the whole path is findable in Application Insights by correlation ID: each Tracking event in API Management, the Carrier-
         tracking Logic App and the TMS, and the Shipper notification and the cancellation in their Logic Apps and the TMS.
    A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS's queries answer only a principal
    holding one of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the TMS as the
    intake identity. It prints only what the checks need: never a body, as the Transport Order holds the Consignee's personal
    data, and never a token or a key. Check 8 stops the endpoint by pointing the Logic App's PARTNER_PORTAL_BASE_URL at an address
    that answers nothing, and restores it afterwards, even when the run fails. What only a Persona's page shows (the Shipper's
    notification inbox, Kessler Spedition's "cancelled by Norvia") the script lists at the end for the presenter to open.
    The presenter's steps are in docs/processes/tms/p7-exception-handling.md.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string] $Story25 = '',
    [Parameter(Position = 1)][string] $Story26 = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$vanDijkId = 'CAR-0001'
$kesslerId = 'CAR-0002'

if (-not ($Story25 -match '^TO-[0-9]+$' -and $Story26 -match '^TO-[0-9]+$' -and $Story25 -ne $Story26)) {
    Write-Fail 'usage: smoke-test-exception-handling.ps1 <story 25 Transport Order number> <story 26 Transport Order number>, such as TO-0000042 TO-0000043'
}
if ([Console]::IsInputRedirected) { Write-Fail "the Planner's actions are the presenter's, so run this in a terminal" }

$arm = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers"
$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
function Get-PersonaKey([string] $Persona) {
    (Invoke-Az rest --method post --url "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/$Persona/listSecrets?api-version=2024-05-01" `
            --query primaryKey --output tsv | Out-String).Trim()
}
Write-Host "TMS: $tms"
Write-Host "Story 25 Transport Order: $Story25"
Write-Host "Story 26 Transport Order: $Story26"

$trackingLogicApp = $env:AZURE_ESTATE_TRACKING_LOGIC_APP
$notificationLogicApp = $env:AZURE_ESTATE_SHIPPER_NOTIFICATION_LOGIC_APP
$cancellationLogicApp = $env:AZURE_ESTATE_CARRIER_CANCELLATION_LOGIC_APP
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# Runs the Carrier's calls in a throwaway Container Apps job, then reads the Shipment's state, and returns what it saw. The
# steps are {name, statusCode, etaDays (or null), wait (the Shipment status to wait for, or null)}. $OffsetMinutes is added to
# the event times, $WaitForException is the Exception type to wait for after the last step (or '').
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

def open_exceptions(shipment_number):
    listed = tms("/v1/exceptions/open")
    if not isinstance(listed, list):
        return listed
    return [{"exceptionNumber": e["exceptionNumber"], "type": e["type"], "causes": e["numberOfCauses"], "causeCleared": e["causeCleared"],
             "afterDelivered": e["afterDelivered"], "lastNotification": e.get("lastShipperNotificationStatus")}
            for e in listed if e["shipmentNumber"] == shipment_number]

order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
shipment_number = order.get("shipmentNumber")
if not shipment_number:
    print("SMOKE-RESULT " + json.dumps({"shipmentNumber": None}))
    raise SystemExit(0)
shipment = tms("/v1/shipments/" + shipment_number)
confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
result = {"shipmentNumber": shipment_number, "carrierId": (shipment.get("carriedBy", {}).get("carrier") or {}).get("id"),
          "reference": confirmed[0]["carrierBookingReference"] if confirmed else None, "steps": []}

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2) + timedelta(minutes=int(os.environ["OFFSET_MINUTES"]))
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    event = {"bookingRef": result["reference"], "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["statusCode"],
             "eventTime": (base + timedelta(minutes=10 * index)).isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
    if step["etaDays"] is not None:
        event["eta"] = (datetime.now(timezone.utc).replace(microsecond=0) + timedelta(days=step["etaDays"])).strftime("%Y-%m-%dT%H:%M:%SZ")
    status, headers, _ = call("POST", os.environ["PARTNER_API"] + "/carrier-tracking", {
        "Authorization": "Bearer " + os.environ["PERSONA_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["PERSONA_KEY"],
        "Content-Type": "application/json"}, event)
    seen = {"name": step["name"], "httpStatus": status, "correlationId": headers.get("x-correlation-id")}
    if step["wait"]:
        for _ in range(24):
            if tms("/v1/shipments/" + shipment_number).get("status") == step["wait"]:
                break
            time.sleep(5)
    result["steps"].append(seen)

wanted = os.environ["WAIT_FOR_EXCEPTION"]
for _ in range(24 if wanted else 0):
    found = open_exceptions(shipment_number)
    if isinstance(found, list) and any(e["type"] == wanted for e in found):
        break
    time.sleep(5)

# The Shipment as the Planner sees it now.
shipment = tms("/v1/shipments/" + shipment_number)
to_release = tms("/v1/shipments/to-release")
released = [s for s in to_release if s["shipmentNumber"] == shipment_number] if isinstance(to_release, list) else []
outcome = (released[0].get("lastCarrierBookingOutcome") or {}) if released else {}
result["status"] = shipment.get("status")
result["carrierId"] = (shipment.get("carriedBy", {}).get("carrier") or {}).get("id")
result["carrierBookings"] = [{"number": b["carrierBookingNumber"], "carrierId": b["carrier"]["id"], "status": b["status"]}
                             for b in shipment.get("carrierBookings", [])]
result["toRelease"] = {"listed": bool(released), "status": outcome.get("status"), "reasonText": outcome.get("reasonText"),
                       "carrierNotTold": outcome.get("carrierNotTold")}
result["exceptions"] = open_exceptions(shipment_number)
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
function Invoke-Steps($Steps, [string] $Number, [string] $Persona, [int] $OffsetMinutes, [string] $WaitForException) {
    Invoke-ThrowawayJobForResult -Prefix smoke-exception-handling -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 600 `
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
            WAIT_FOR_EXCEPTION       = $WaitForException
            STEPS                    = ConvertTo-Json -InputObject @($Steps) -Compress -Depth 5
        })
}
# The Shipment's state without sending anything, for what the presenter just did.
function Get-Look([string] $Number, [string] $Persona, [string] $WaitForException = '') { Invoke-Steps @() $Number $Persona 0 $WaitForException }

# Asks again every 10 seconds, up to $Attempts times, until $Test is true of the look; returns the last look either way.
function Wait-Look([string] $Number, [string] $Persona, [scriptblock] $Test, [int] $Attempts = 12) {
    $look = Get-Look $Number $Persona
    for ($i = 1; $i -lt $Attempts -and -not (& $Test $look); $i++) {
        Start-Sleep -Seconds 10
        $look = Get-Look $Number $Persona
    }
    $look
}

function Get-OpenException($Result, [string] $Type) { @($Result.exceptions) | Where-Object { $_.type -eq $Type } | Select-Object -First 1 }
function Get-Step($Result, [string] $Name) { $Result.steps | Where-Object name -EQ $Name | Select-Object -First 1 }
function Assert-StepAccepted($Result, [string] $Name, [string] $Who) {
    $step = Get-Step $Result $Name
    if ($step.httpStatus -ne 202) { Write-Fail "$Who's $Name didn't get 202 Accepted: $($step | ConvertTo-Json -Compress)" }
}
function Assert-Published([string] $Shipment, [string] $EventName) {
    $name = Wait-Until { Get-PublishedEvent -WorkspaceId $workspace -Shipment $Shipment -EventName $EventName }
    if (-not $name) { Write-Fail "no Message trail of Shipment $Shipment's Planner actions published $EventName in Application Insights after 10 minutes" }
}

# Put back whatever the Shipper notification Logic App was calling, if the run ends while it is pointed away from the Partner portal.
$restorePortal = $null
try {
    # Story 25. 1. PU, DEP and NDL as Van Dijk Transport.
    Write-Host ''
    Write-Host 'Story 25'
    $result = Invoke-Steps -Number $Story25 -Persona van-dijk-transport -OffsetMinutes 0 -WaitForException DeliveryFailed -Steps @(
        @{ name = 'PU'; statusCode = 'PU'; etaDays = $null; wait = 'Collected' }
        @{ name = 'DEP'; statusCode = 'DEP'; etaDays = $null; wait = 'In transit' }
        @{ name = 'NDL'; statusCode = 'NDL'; etaDays = $null; wait = $null })
    $shipment25 = $result.shipmentNumber
    if (-not $shipment25) { Write-Fail "Transport Order $Story25 has no Shipment, or the TMS doesn't answer for it" }
    if (-not ($result.carrierId -eq $vanDijkId -and $null -ne $result.reference)) {
        Write-Fail "Shipment $shipment25 isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(@{ status = $result.status; carrierId = $result.carrierId; reference = $result.reference } | ConvertTo-Json -Compress)"
    }
    foreach ($code in 'PU', 'DEP', 'NDL') { Assert-StepAccepted $result $code 'Van Dijk Transport' }
    $ndlId = (Get-Step $result NDL).correlationId
    Write-Pass "PU, DEP and NDL returned 202 Accepted (NDL's correlation ID $ndlId)"
    if ($result.status -ne 'In transit') { Write-Fail "Shipment $shipment25 isn't In transit: $($result.status)" }
    $failed = @(@($result.exceptions) | Where-Object { $_.type -eq 'DeliveryFailed' })
    if (-not ($failed.Count -eq 1 -and -not $failed[0].afterDelivered)) {
        Write-Fail "Shipment $shipment25 doesn't have exactly one Open Delivery failed Exception, opened before Delivered: $($result.exceptions | ConvertTo-Json -Compress)"
    }
    $exception25 = $failed[0].exceptionNumber
    Write-Pass "NDL opened Exception $exception25 (Delivery failed) for Shipment $shipment25"

    # 2 and 3. The presenter notifies the Shipper and agrees a new window.
    Wait-ForPresenter "In the TMS UI ($tmsUi/exceptions), on Exception ${exception25}: choose Notify Shipper and send the message as it is,`nthen Agree delivery window with a latest time in the future."
    $result = Wait-Look $Story25 van-dijk-transport { param($r) (Get-OpenException $r DeliveryFailed).lastNotification -eq 'Sent' }
    if ((Get-OpenException $result DeliveryFailed).lastNotification -ne 'Sent') {
        Write-Fail "Exception $exception25's last Shipper notification isn't Sent: $($result.exceptions | ConvertTo-Json -Compress)"
    }
    Write-Pass "Exception $exception25's Shipper notification was Sent: Brouwer Dranken's endpoint acknowledged it"
    Assert-Published $shipment25 ShipmentDeliveryWindowAgreed
    Write-Pass 'the agreed delivery window published ShipmentDeliveryWindowAgreed'

    # 4. POD delivers the Shipment, and the cause clears.
    $result = Invoke-Steps -Number $Story25 -Persona van-dijk-transport -OffsetMinutes 60 -WaitForException '' -Steps @(
        @{ name = 'POD'; statusCode = 'POD'; etaDays = $null; wait = 'Delivered' })
    Assert-StepAccepted $result POD 'Van Dijk Transport'
    if (-not ($result.status -eq 'Delivered' -and (Get-OpenException $result DeliveryFailed).causeCleared -eq $true)) {
        Write-Fail "after POD Shipment $shipment25 isn't Delivered with its Delivery failed Exception marked cause cleared: $(@{ status = $result.status; exceptions = $result.exceptions } | ConvertTo-Json -Compress -Depth 5)"
    }
    Write-Pass "POD returned 202 Accepted: Shipment $shipment25 is Delivered and Exception $exception25 is marked cause cleared, still Open"
    $podId = (Get-Step $result POD).correlationId

    # 5. The presenter resolves it.
    Wait-ForPresenter "In the TMS UI, resolve Exception $exception25 as Redelivered, with a remark."
    $result = Get-Look $Story25 van-dijk-transport
    if (Get-OpenException $result DeliveryFailed) { Write-Fail "Exception $exception25 is still in the list of Open Exceptions: $($result.exceptions | ConvertTo-Json -Compress)" }
    $resolved = Wait-Until {
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "AppTraces | where tostring(Properties.shipmentNumber) == '$shipment25' and Message has 'resolved as Redelivered' | distinct Message"
        $rows.Count -gt 0 ? $rows[0][0] : $null
    }
    if (-not $resolved) { Write-Fail "no 'resolved as Redelivered' for Shipment $shipment25 in Application Insights after 10 minutes" }
    Assert-Published $shipment25 ExceptionResolved
    Write-Pass "Exception $exception25 was resolved as Redelivered, left the list and published ExceptionResolved"

    # Story 26. 6. A far ETA from Kessler Spedition on a Booked Shipment. An NPU carries it without moving the Shipment: every
    # other code moves the status forward, and a Cancel is allowed only while the Shipment is Booked.
    Write-Host ''
    Write-Host 'Story 26'
    $result = Invoke-Steps -Number $Story26 -Persona kessler-spedition -OffsetMinutes 0 -WaitForException LateDelivery -Steps @(
        @{ name = 'ETA'; statusCode = 'NPU'; etaDays = 14; wait = $null })
    $shipment26 = $result.shipmentNumber
    if (-not $shipment26) { Write-Fail "Transport Order $Story26 has no Shipment, or the TMS doesn't answer for it" }
    if (-not ($result.carrierId -eq $kesslerId -and $null -ne $result.reference)) {
        Write-Fail "Shipment $shipment26 isn't carried by Kessler Spedition with a Confirmed Carrier booking: $(@{ status = $result.status; carrierId = $result.carrierId; reference = $result.reference } | ConvertTo-Json -Compress)"
    }
    Assert-StepAccepted $result ETA 'Kessler Spedition'
    $etaId = (Get-Step $result ETA).correlationId
    $late = @(@($result.exceptions) | Where-Object { $_.type -eq 'LateDelivery' })
    if (-not ($result.status -eq 'Booked' -and $late.Count -eq 1)) {
        Write-Fail "Shipment $shipment26 isn't still Booked with an Open Late delivery Exception after the far ETA: $(@{ status = $result.status; exceptions = $result.exceptions } | ConvertTo-Json -Compress -Depth 5)"
    }
    $exception26 = $late[0].exceptionNumber
    $kesslerBooking = (@($result.carrierBookings) | Where-Object { $_.carrierId -eq $kesslerId -and $_.status -eq 'Confirmed' } | Select-Object -First 1).number
    if (-not $kesslerBooking) { Write-Fail "Shipment $shipment26 has no Confirmed Carrier booking of Kessler Spedition: $($result.carrierBookings | ConvertTo-Json -Compress)" }
    Write-Pass "a far ETA opened Exception $exception26 (Late delivery) for Booked Shipment $shipment26 (correlation ID $etaId)"

    # 7. The presenter cancels, and releases again to Van Dijk Transport.
    Wait-ForPresenter "In the TMS UI ($tmsUi/exceptions), on Exception ${exception26}: choose Cancel Carrier booking with a reason. Stop there:`nthe script reads Shipments to release before the Shipment is released again."
    $result = Wait-Look $Story26 kessler-spedition { param($r) (@($r.carrierBookings) | Where-Object number -EQ $kesslerBooking).status -eq 'Cancelled' }
    if ((@($result.carrierBookings) | Where-Object number -EQ $kesslerBooking).status -ne 'Cancelled') {
        Write-Fail "Kessler Spedition's Carrier booking $kesslerBooking isn't Cancelled: $($result.carrierBookings | ConvertTo-Json -Compress)"
    }
    Write-Pass "Kessler Spedition's Carrier booking $kesslerBooking is Cancelled and Shipment $shipment26 is $($result.status)"
    if (-not $result.toRelease.listed) { Write-Fail "Shipment $shipment26 isn't in Shipments to release after the cancellation" }
    if (-not ($result.toRelease.status -eq 'Cancelled' -and $result.toRelease.reasonText -and -not $result.toRelease.carrierNotTold)) {
        Write-Fail "Shipment $shipment26's Shipments to release row isn't Cancelled by Norvia with its reason, Carrier told: $($result.toRelease | ConvertTo-Json -Compress)"
    }
    Write-Pass "Shipment $shipment26 is in Shipments to release as Cancelled by Norvia, with its reason, and the Carrier isn't flagged not told"
    Wait-ForPresenter "Now reassign Shipment $shipment26 to Van Dijk Transport and release it."
    $result = Get-Look $Story26 kessler-spedition
    if (@(@($result.carrierBookings) | Where-Object carrierId -EQ $vanDijkId).Count -lt 1) {
        Write-Fail "Shipment $shipment26 wasn't released again to Van Dijk Transport: $($result.carrierBookings | ConvertTo-Json -Compress)"
    }
    Write-Pass "Shipment $shipment26 was released again to Van Dijk Transport"

    # 8. With the Shipper notification endpoint stopped, a notification ends Not delivered after the retries.
    $portal = (Invoke-Az logicapp config appsettings list --subscription $subscription --name $notificationLogicApp --resource-group $resourceGroup `
            --query "[?name=='PARTNER_PORTAL_BASE_URL'].value | [0]" --output tsv | Out-String).Trim()
    if (-not $portal) { Write-Fail 'the Shipper notification Logic App has no PARTNER_PORTAL_BASE_URL to put back' }
    Write-Host ''
    Write-Host 'Stopping the Shipper notification endpoint (pointing the Logic App away from the Partner portal)...'
    $restorePortal = $portal
    Invoke-Az logicapp config appsettings set --subscription $subscription --name $notificationLogicApp --resource-group $resourceGroup `
        --settings 'PARTNER_PORTAL_BASE_URL=https://partner-portal.stopped.invalid' --output none | Out-Null
    Start-Sleep -Seconds 60
    # Story 26's Exception is still open: a notification can be requested from it.
    Wait-ForPresenter "In the TMS UI, on Exception ${exception26}: choose Notify Shipper and send the message as it is."
    $result = Wait-Look $Story26 kessler-spedition -Attempts 18 { param($r) (Get-OpenException $r LateDelivery).lastNotification -eq 'NotDelivered' }
    if ((Get-OpenException $result LateDelivery).lastNotification -ne 'NotDelivered') {
        Write-Fail "with the endpoint stopped, Exception $exception26's last Shipper notification isn't Not delivered after the retries: $($result.exceptions | ConvertTo-Json -Compress)"
    }
    Write-Pass "with the Shipper notification endpoint stopped, Exception $exception26's notification ended Not delivered after the retries"
    Invoke-Az logicapp config appsettings set --subscription $subscription --name $notificationLogicApp --resource-group $resourceGroup `
        --settings "PARTNER_PORTAL_BASE_URL=$portal" --output none | Out-Null
    $restorePortal = $null

    # 9. The whole path, by correlation ID.
    Write-Host ''
    $trackingRoles = $env:AZURE_ESTATE_API_MANAGEMENT, $trackingLogicApp, 'tms-api'
    Assert-InApplicationInsights -WorkspaceId $workspace -What "NDL, by correlation ID $ndlId," -Value $ndlId -Roles $trackingRoles
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the far ETA, by correlation ID $etaId," -Value $etaId -Roles $trackingRoles
    Assert-InApplicationInsights -WorkspaceId $workspace -What "POD, by correlation ID $podId," -Value $podId -Roles $trackingRoles
    $notificationIds = Wait-Until { Get-ActionIdsReaching -WorkspaceId $workspace -Shipment $shipment25 -Role $notificationLogicApp }
    if (-not $notificationIds) { Write-Fail "no Planner action on Shipment $shipment25 reached the Shipper notification Logic App in Application Insights after 10 minutes" }
    $notificationId = @($notificationIds)[0]
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the Shipper notification, by correlation ID $notificationId," -Value $notificationId -Roles $notificationLogicApp, 'tms-api'
    $cancellationIds = Wait-Until { Get-ActionIdsReaching -WorkspaceId $workspace -Shipment $shipment26 -Role $cancellationLogicApp }
    if (-not $cancellationIds) { Write-Fail "no Planner action on Shipment $shipment26 reached the Carrier cancellation Logic App in Application Insights after 10 minutes" }
    $cancellationId = @($cancellationIds)[0]
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the cancellation, by correlation ID $cancellationId," -Value $cancellationId -Roles $cancellationLogicApp, 'tms-api'

    Write-Host ''
    Write-Host "Open in the Partner portal, as the Personas (the script can't sign in as them):"
    Write-Host "  Brouwer Dranken's notification inbox: the notification for Exception $exception25's message, newest first"
    Write-Host "  Kessler Spedition's booking inbox: the request for Shipment $shipment26 as `"cancelled by Norvia`", with the reason"
    Write-Host 'Open in the TMS UI:'
    Write-Host "  Shipment 25: $tmsUi/shipments/$shipment25"
    Write-Host "  Shipment 26: $tmsUi/shipments/$shipment26"
    Write-Host 'Open in the Integration console, by the Shipment number and by each correlation ID:'
    Write-Host "  Shipment 25: $console/?key=$shipment25"
    Write-Host "  Shipment 26: $console/?key=$shipment26"
    Write-Host "  NDL:         $console/?key=$ndlId"
    Write-Host "  Far ETA:     $console/?key=$etaId"
    Write-Host "  POD:         $console/?key=$podId"
}
finally {
    if ($restorePortal) {
        try {
            Invoke-Az logicapp config appsettings set --subscription $subscription --name $notificationLogicApp --resource-group $resourceGroup `
                --settings "PARTNER_PORTAL_BASE_URL=$restorePortal" --output none | Out-Null
        }
        catch { Write-Host "Put the Shipper notification Logic App's PARTNER_PORTAL_BASE_URL back to $restorePortal" }
    }
}
