#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Proof of delivery (P6.11, not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of Proof of delivery (P6.11, not in CI). Book Rotterdam to Düsseldorf as Brouwer Dranken
    and get its Shipment Booked and carried by Van Dijk Transport, as in the presenter smoke run of Carrier booking
    (docs/processes/tms/p4-carrier-booking.md, which leaves Kessler Spedition in Auto-reject; restore it afterwards), then run
    this, signed in to the Azure CLI as the presenter the estate was provisioned for, with its Transport Order number. Do not
    track it first: this delivers it. The Persona's Auto-progress, if it is on, would send a Proof of delivery of its own.

      infra/smoke-test-proof-of-delivery.ps1 <Transport Order number>

    It checks that:
      1. as Van Dijk Transport, through API Management, PU, DEP and POD each return 202 Accepted and the Shipment is Delivered,
         listed in the Planner's 'Delivered without Proof of delivery';
      2. a Proof of delivery with the sample CMR returns 202 Accepted, the Shipment leaves that list, and the Proof of delivery
         is on its timeline as the current one, with its file;
      3. the Proof of delivery's blob is gone from the inbound container;
      4. a Proof of delivery for a reference no Carrier booking has is accepted, then lands on the Proof of delivery queue's
         dead-letter queue with ShipmentNotFound, and its blob is still in the inbound container;
      5. a body over 25 MB is refused with 413;
      6. each call is findable in Application Insights by its correlation ID: in API Management, in the Carrier Proof of
         delivery Logic App and in the TMS, and for the refused body in API Management.
    A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS's queries answer only a
    principal holding one of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the
    TMS as the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a body,
    as the Transport Order holds the Consignee's personal data, and never a token or a key. The queue's dead-letter queue is
    read with the presenter's own Azure Service Bus Data Receiver right, by locking a message and releasing it again, and the
    inbound container with the presenter's own Storage Blob Data Reader right. The presenter's steps are in
    docs/processes/tms/p6-proof-of-delivery.md.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Number = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$vanDijkId = 'CAR-0001'
$queue = 'carrier-proof-of-delivery'
$namespace = $env:AZURE_ESTATE_SERVICE_BUS_NAMESPACE
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sampleCmr = Join-Path $repo 'src/PartnerPortal/Norvia.PartnerPortal.Web/CarrierShipments/sample-cmr.pdf'

if ($Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-proof-of-delivery.ps1 <Transport Order number>, such as TO-0000042' }
if (-not (Test-Path $sampleCmr -PathType Leaf)) { Write-Fail "the sample CMR isn't at $sampleCmr" }

$arm = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers"
$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
$vanDijkClientId = Get-PersonaClientId van-dijk-transport
$vanDijkKey = (Invoke-Az rest --method post --url "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/van-dijk-transport/listSecrets?api-version=2024-05-01" `
        --query primaryKey --output tsv | Out-String).Trim()
$proofLogicApp = $env:AZURE_ESTATE_PROOF_OF_DELIVERY_LOGIC_APP
$inboundAccount = $env:AZURE_ESTATE_INBOUND_STORAGE_ACCOUNT
$inboundContainer = $env:AZURE_ESTATE_INBOUND_CONTAINER
$cmrBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($sampleCmr))
$cmrBytes = (Get-Item $sampleCmr).Length
Write-Host "TMS: $tms"
Write-Host "Transport Order: $Number"

$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# Runs the Carrier's calls in a throwaway Container Apps job and returns what it saw. The steps are {name, kind, ...}, where
# kind is track (statusCode, wait: the status to wait for), proof (bookingRef, null for the Shipment's own, and the document
# ID's suffix, which is waited for on the timeline when await is true) or oversize.
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
    data = None if body is None else (body if isinstance(body, bytes) else json.dumps(body).encode())
    request = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            text = response.read()
            return response.status, response.headers, (json.loads(text) if text else None)
    except urllib.error.HTTPError as error:
        return error.code, error.headers, None
    except OSError:
        return None, {}, None

def tms(path):
    status, _, body = call("GET", os.environ["TMS"] + path, {"Authorization": "Bearer " + os.environ["TMS_TOKEN"]})
    return body if status == 200 else {"httpStatus": status}

def partner(path, body):
    return call("POST", os.environ["PARTNER_API"] + path, {
        "Authorization": "Bearer " + os.environ["PERSONA_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["VAN_DIJK_KEY"],
        "Content-Type": "application/json"}, body)

def is_listed(shipment_number):
    listed = tms("/v1/shipments/delivered-without-proof-of-delivery")
    return isinstance(listed, list) and any(s["shipmentNumber"] == shipment_number for s in listed)

order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
if not order.get("shipmentNumber"):
    print("SMOKE-RESULT " + json.dumps({"shipmentNumber": None}))
    raise SystemExit(0)
shipment_number = order.get("shipmentNumber")
shipment = tms("/v1/shipments/" + shipment_number)
confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
result = {"shipmentNumber": shipment_number, "status": shipment.get("status"), "carrierId": (shipment.get("carriedBy", {}).get("carrier") or {}).get("id"),
          "reference": confirmed[0]["carrierBookingReference"] if confirmed else None, "steps": []}
if result["status"] != "Booked":
    print("SMOKE-RESULT " + json.dumps(result))
    raise SystemExit(0)

# Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past, and never later than the next.
base = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2)
run = os.environ["RUN"]
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    seen = {"name": step["name"]}
    if step["kind"] == "track":
        event = {"bookingRef": result["reference"], "eventId": "SMOKE-%s-%d" % (run, index), "statusCode": step["statusCode"],
                 "eventTime": (base + timedelta(minutes=10 * index)).isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
        status, headers, _ = partner("/carrier-tracking", event)
        seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"))
        for _ in range(24):
            now = tms("/v1/shipments/" + shipment_number)
            if now.get("status") == step["wait"]:
                break
            time.sleep(5)
        seen["status"] = now.get("status")
        if step["wait"] == "Delivered":
            seen["listed"] = is_listed(shipment_number)
    elif step["kind"] == "proof":
        document_id = "SMOKE-%s-%s" % (run, step["name"])
        proof = {"bookingRef": step["bookingRef"] or result["reference"], "documentId": document_id, "receivedBy": "A. Schmitt",
                 "deliveryTime": (base + timedelta(minutes=30)).isoformat(), "place": "Düsseldorf", "postcode": "40591", "country": "DEU",
                 "condition": "CLEAN", "files": [{"name": "sample-cmr.pdf", "contentType": "application/pdf", "content": os.environ["CMR_BASE64"]}]}
        status, headers, _ = partner("/carrier-proofs-of-delivery", proof)
        seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"), documentId=document_id)
        if step["await"]:
            for _ in range(36):
                timeline = tms("/v1/shipments/%s/tracking-events" % shipment_number)
                if isinstance(timeline, dict) and any(p["documentId"] == document_id for p in timeline.get("proofsOfDelivery", [])):
                    break
                time.sleep(5)
            # The list reads what the timeline does, but is asked again for a few seconds before it is taken as still listed.
            for _ in range(6):
                seen["listed"] = is_listed(shipment_number)
                if not seen["listed"]:
                    break
                time.sleep(5)
    else:
        # Just over the 25 MB API Management allows, built in memory (hence the long job timeout); the body is never parsed, so it
        # need not be a valid Proof of delivery.
        status, headers, _ = partner("/carrier-proofs-of-delivery", ("{\"documentId\": \"SMOKE-%s-oversize\", \"padding\": \"%s\"}" % (run, "A" * (25 * 1024 * 1024 + 1))).encode())
        seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"))
    result["steps"].append(seen)

timeline = tms("/v1/shipments/%s/tracking-events" % shipment_number)
result["proofs"] = [{"documentId": p["documentId"], "current": p["isCurrent"], "files": [{"name": f["name"], "contentType": f["contentType"], "sizeBytes": f["sizeBytes"]} for f in p["files"]]}
                    for p in timeline.get("proofsOfDelivery", [])] if isinstance(timeline, dict) else None
print("SMOKE-RESULT " + json.dumps(result))
PYTHON
'@
function Invoke-Steps($Steps) {
    Invoke-ThrowawayJobForResult -Prefix smoke-proof-of-delivery -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 900 `
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
            CMR_BASE64               = $cmrBase64
            STEPS                    = ConvertTo-Json -InputObject @($Steps) -Compress -Depth 5
        })
}
function Get-StepOf($Result, [string] $Name) { $Result.steps | Where-Object name -EQ $Name | Select-Object -First 1 }

# The inbound container's blob of a correlation ID, as the presenter, who can only read: 'exists' or 'gone', and $null when
# the container can't be read, which is never taken for gone.
function Get-BlobState([string] $Name) {
    $exists = (Invoke-Az storage blob exists --account-name $inboundAccount --container-name $inboundContainer --name $Name `
            --auth-mode login --query exists --output tsv -AllowFailure | Out-String).Trim()
    switch ($exists) { 'true' { 'exists' } 'false' { 'gone' } }
}

# Deliver the Booked Shipment, send its Proof of delivery, then one for a reference that no Carrier booking has, then a body
# that is too large.
$result = Invoke-Steps @(
    @{ name = 'PU'; kind = 'track'; statusCode = 'PU'; wait = 'Collected' }
    @{ name = 'DEP'; kind = 'track'; statusCode = 'DEP'; wait = 'In transit' }
    @{ name = 'POD'; kind = 'track'; statusCode = 'POD'; wait = 'Delivered' }
    @{ name = 'cmr'; kind = 'proof'; bookingRef = $null; await = $true }
    @{ name = 'unknown'; kind = 'proof'; bookingRef = 'SMOKE-NO-SUCH-REFERENCE'; await = $false }
    @{ name = 'oversize'; kind = 'oversize' })
$shipmentNumber = $result.shipmentNumber
if (-not $shipmentNumber) { Write-Fail "Transport Order $Number has no Shipment, or the TMS doesn't answer for it" }
if (-not ($result.carrierId -eq $vanDijkId -and $null -ne $result.reference)) {
    Write-Fail "Shipment $shipmentNumber isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(@{ status = $result.status; carrierId = $result.carrierId; reference = $result.reference } | ConvertTo-Json -Compress)"
}
if (-not ($result.status -eq 'Booked' -and @($result.steps).Count -gt 0)) { Write-Fail "Shipment $shipmentNumber is $($result.status), not Booked: book a new one, as this run delivers it" }

# 1. Delivered, and listed without a Proof of delivery.
foreach ($expected in @(@('PU', 'Collected'), @('DEP', 'In transit'), @('POD', 'Delivered'))) {
    $code, $status = $expected
    $step = Get-StepOf $result $code
    if (-not ($step.httpStatus -eq 202 -and $step.status -eq $status)) {
        Write-Fail "Van Dijk Transport's $code didn't get 202 Accepted and move Shipment $shipmentNumber to ${status}: $($step | ConvertTo-Json -Compress)"
    }
    Write-Pass "$code returned 202 Accepted and Shipment $shipmentNumber is $status"
}
if (-not (Get-StepOf $result POD).listed) { Write-Fail "Delivered Shipment $shipmentNumber isn't in 'Delivered without Proof of delivery'" }
Write-Pass "Shipment $shipmentNumber is in 'Delivered without Proof of delivery'"

# 2. The Proof of delivery with the sample CMR: accepted, off the list, and on the timeline with its file.
$cmr = Get-StepOf $result cmr
$cmrId = $cmr.correlationId
if ($cmr.httpStatus -ne 202) { Write-Fail "the Proof of delivery with the sample CMR didn't get 202 Accepted: $($cmr | ConvertTo-Json -Compress)" }
if (-not $cmrId) { Write-Fail "API Management's 202 for the Proof of delivery carries no correlation ID" }
Write-Pass "the Proof of delivery with the sample CMR returned 202 Accepted (correlation ID $cmrId)"
if ($cmr.listed -ne $false) { Write-Fail "Shipment $shipmentNumber is still in 'Delivered without Proof of delivery' once its Proof of delivery was on the timeline" }
Write-Pass "Shipment $shipmentNumber has left 'Delivered without Proof of delivery'"
$proofs = @($result.proofs | Where-Object {
        $_.documentId -eq $cmr.documentId -and $_.current -and @($_.files).Count -eq 1 -and $_.files[0].name -eq 'sample-cmr.pdf' `
            -and $_.files[0].contentType -eq 'application/pdf' -and $_.files[0].sizeBytes -eq $cmrBytes })
if ($proofs.Count -eq 0) { Write-Fail "Shipment $shipmentNumber's timeline doesn't hold the current Proof of delivery with the sample CMR: $($result.proofs | ConvertTo-Json -Compress -Depth 5)" }
Write-Pass "Shipment $shipmentNumber's timeline holds the Proof of delivery as the current one, with sample-cmr.pdf ($cmrBytes bytes)"

# 3. The blob is gone once the TMS has it.
# Waits for the blob to be gone, which the Logic App does once the TMS has accepted the Proof of delivery.
$gone = Wait-Until { (Get-BlobState $cmrId) -eq 'gone' ? 'gone' : $null }
if ($gone -ne 'gone') { Write-Fail "the Proof of delivery's blob $cmrId is still in the $inboundContainer container after the wait, or the presenter can't read it" }
Write-Pass "the Proof of delivery's blob is gone from the $inboundContainer container"

# 4. The unknown reference is accepted, then dead-lettered with ShipmentNotFound, and its blob stays for investigation.
$unknown = Get-StepOf $result unknown
$unknownId = $unknown.correlationId
if ($unknown.httpStatus -ne 202) { Write-Fail "the Proof of delivery for an unknown reference didn't get 202 Accepted: $($unknown | ConvertTo-Json -Compress)" }
if (-not $unknownId) { Write-Fail "API Management's 202 for the unknown reference carries no correlation ID" }
Write-Pass "the Proof of delivery for an unknown reference returned 202 Accepted (correlation ID $unknownId)"
$reason = Wait-Until { Get-DeadLetterReason -Namespace $namespace -Queue $queue -CorrelationId $unknownId }
if ($reason -ne 'ShipmentNotFound') { Write-Fail "the unknown reference's message isn't on the $queue dead-letter queue with ShipmentNotFound after 10 minutes: $($reason ? $reason : 'not there')" }
Write-Pass "the unknown reference's message is on the $queue dead-letter queue, dead-lettered with ShipmentNotFound"
if ((Get-BlobState $unknownId) -ne 'exists') { Write-Fail "the unknown reference's blob $unknownId isn't in the $inboundContainer container, or the presenter can't read it" }
Write-Pass "the unknown reference's blob is still in the $inboundContainer container"

# 5. Too large.
$oversize = Get-StepOf $result oversize
$oversizeId = $oversize.correlationId
if ($oversize.httpStatus -ne 413) { Write-Fail "a body over 25 MB wasn't refused with 413: $($oversize | ConvertTo-Json -Compress)" }
Write-Pass "a body over 25 MB was refused with 413 (correlation ID $($oversizeId ? $oversizeId : 'none'))"

# 6. Each call by its correlation ID. Azure Monitor names the TMS after its Container Apps environment too:
# [<environment>]/tms-api.
$roles = $env:AZURE_ESTATE_API_MANAGEMENT, $proofLogicApp, 'tms-api'
Assert-InApplicationInsights -WorkspaceId $workspace -What "the Proof of delivery, by correlation ID $cmrId," -Value $cmrId -Roles $roles
Assert-InApplicationInsights -WorkspaceId $workspace -What "the unknown reference's Proof of delivery, by correlation ID $unknownId," -Value $unknownId -Roles $roles
if (-not $oversizeId) { Write-Fail "API Management's 413 carries no correlation ID" }
Assert-InApplicationInsights -WorkspaceId $workspace -What "the refused body, by correlation ID $oversizeId," -Value $oversizeId -Roles $env:AZURE_ESTATE_API_MANAGEMENT

$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
Write-Host ''
Write-Host 'Open in the TMS UI:'
Write-Host "  Shipment:  $tmsUi/shipments/$shipmentNumber"
Write-Host 'Open in the Integration console, by the Shipment number and by each correlation ID:'
Write-Host "  Shipment:  $console/?key=$shipmentNumber"
Write-Host "  Proof of delivery: $console/?key=$cmrId"
Write-Host "  Unknown reference: $console/?key=$unknownId"
Write-Host "  Too large: $console/?key=$oversizeId"
