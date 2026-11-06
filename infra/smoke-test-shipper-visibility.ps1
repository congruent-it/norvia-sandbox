#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Shipper visibility (P8.8, not in CI).
.DESCRIPTION
    Post-deploy presenter smoke run of Shipper visibility (P8.8, not in CI). It re-enacts the headline incident from the Shipper's
    side: user story 14 (Brouwer Dranken watches its tracking screen while Kessler Spedition in Auto-progress delivers a Transport
    Order) and user story 15 (the same screen stays on "In transit" when a Carrier's status code is outside P5's mapping table).
    The Shipper's screen and the Planner's release are the presenter's, in the Partner portal and the TMS UI; the script stops and
    says what to do. Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for, with two
    Transport Order numbers of Brouwer Dranken:

      infra/smoke-test-shipper-visibility.ps1 <story 14 Transport Order number> <story 15 Transport Order number>

    Get the first Planned and assigned to Kessler Spedition but NOT released, so it is Scheduled and the presenter can release it
    with the screen open (leave Kessler Spedition on Auto-confirm and Auto-progress). Get the second Booked and carried by Van Dijk
    Transport, as in the presenter smoke run of Carrier booking (docs/processes/tms/p4-carrier-booking.md), with Van Dijk
    Transport's tracking on Manual. Do not track either first.
    It checks that:
      1. story 14: through API Management, as Brouwer Dranken, the Transport Order is SCHEDULED; once released it only moves
         forward and ends DELIVERED with a Proof of delivery, whose sample CMR downloads; the presenter saw the screen do it;
      2. a Carrier Persona's token (Van Dijk Transport's) against the tracking API gets 403 Forbidden;
      3. story 15: PU and DEP as Van Dijk Transport return 202 Accepted and the Shipper sees IN_TRANSIT; then a status code outside
         the mapping table (DLV) also returns 202 Accepted, yet the Shipper still sees IN_TRANSIT with the same last update
         a minute and a half later;
      4. with the TMS stopped for API Management, a query gets 503 with a correlation ID, the presenter sees the error with the
         time of the last answer on the screen, and the query works again once it is back;
      5. the queries are findable in Application Insights by correlation ID, in API Management and, where the TMS answered, the TMS.
    A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS answers only a principal holding one
    of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the TMS as the intake
    identity. P5's presenter-panel fault isn't built yet, so check 3 plays what it does: the Carrier sends a code the Carrier-
    tracking Logic App can't map. The script prints only what the checks need: never a body, a token or a key.
    Check 4 stops the TMS by pointing API Management's tms-api backend at an address that answers nothing, and restores it
    afterwards, even when the run fails. The presenter's steps are in docs/processes/tms/p8-shipper-visibility.md.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string] $Story14 = '',
    [Parameter(Position = 1)][string] $Story15 = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$vanDijkId = 'CAR-0001'

if (-not ($Story14 -match '^TO-[0-9]+$' -and $Story15 -match '^TO-[0-9]+$')) {
    Write-Fail 'usage: smoke-test-shipper-visibility.ps1 <story 14 Transport Order number> <story 15 Transport Order number>, such as TO-0000042 TO-0000043'
}
if ([Console]::IsInputRedirected) { Write-Fail "run this in a terminal: the presenter's steps wait for Enter" }

$arm = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers"
$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
function Get-SubscriptionKey([string] $Name) {
    (Invoke-Az rest --method post --url "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/$Name/listSecrets?api-version=2024-05-01" `
            --query primaryKey --output tsv | Out-String).Trim()
}
$brouwerClientId = Get-PersonaClientId brouwer-dranken
$vanDijkClientId = Get-PersonaClientId van-dijk-transport
$brouwerKey = Get-SubscriptionKey brouwer-dranken
$vanDijkKey = Get-SubscriptionKey van-dijk-transport

$backend = "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/backends/tms-api?api-version=2024-05-01"
$backendUrl = (Invoke-Az rest --method get --url $backend --query properties.url --output tsv | Out-String).Trim()
function Set-Backend([string] $Url) {
    Invoke-AzRest -Method patch -Url $backend -Headers 'If-Match=*' -Body @{ properties = @{ url = $Url } } | Out-Null
}
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# Runs a list of steps in a throwaway Container Apps job and returns what it saw. The steps run in order, each with a name:
#   {do: "get", as: "brouwer"|"vandijk", path}                    one call; httpStatus, correlationId, and for an order its status
#   {do: "watch", number, seconds, until}                         polls the Shipper's view every 3 seconds; seen statuses in order
#   {do: "download", number}                                      the first file of the current Proof of delivery
#   {do: "send", code, offset}                                    a tracking event as Van Dijk Transport for Transport Order $NUMBER
$calls = @'
set -eu
token_for() {
  az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
  assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
  curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
    -d grant_type=client_credentials -d client_id="$1" -d scope="$PARTNER_API_CLIENT_ID/.default" \
    -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
    | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])"
}
BROUWER_TOKEN=$(token_for "$BROUWER_CLIENT_ID")
VAN_DIJK_TOKEN=$(token_for "$VAN_DIJK_CLIENT_ID")
export BROUWER_TOKEN VAN_DIJK_TOKEN
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
            return response.status, response.headers, text
    except urllib.error.HTTPError as error:
        return error.code, error.headers, b""
    except OSError:
        return 0, {}, b""

def parse(text):
    try:
        return json.loads(text) if text else None
    except ValueError:
        return None

def edge(who, path):
    token, key = (os.environ["BROUWER_TOKEN"], os.environ["BROUWER_KEY"]) if who == "brouwer" else (os.environ["VAN_DIJK_TOKEN"], os.environ["VAN_DIJK_KEY"])
    return call("GET", os.environ["PARTNER_API"] + path, {"Authorization": "Bearer " + token, "Ocp-Apim-Subscription-Key": key})

def tms(path):
    status, _, text = call("GET", os.environ["TMS"] + path, {"Authorization": "Bearer " + os.environ["TMS_TOKEN"]})
    return parse(text) if status == 200 else {"httpStatus": status}

def view(number):
    status, headers, text = edge("brouwer", "/transport-orders/" + number)
    body = parse(text) if status == 200 else None
    return status, headers.get("x-correlation-id"), body

results = []
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    seen = {"name": step["name"]}
    if step["do"] == "get":
        status, headers, text = edge(step["as"], step["path"])
        body = parse(text) if status == 200 else None
        seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"))
        if isinstance(body, dict):
            seen["status"] = body.get("status")
    elif step["do"] == "watch":
        statuses, last_updates, started = [], [], time.time()
        while time.time() - started < step["seconds"]:
            status, correlation_id, body = view(step["number"])
            if body:
                if not statuses or statuses[-1] != body["status"]:
                    statuses.append(body["status"])
                if not last_updates or last_updates[-1] != body.get("lastUpdate"):
                    last_updates.append(body.get("lastUpdate"))
                seen["correlationId"] = correlation_id
                seen["files"] = [f["fileId"] for f in (body.get("proofOfDelivery") or {}).get("files", [])]
                seen["codes"] = [e["code"] for e in body.get("events", [])]
                if step["until"] and body["status"] == step["until"]:
                    break
            time.sleep(3)
        seen.update(statuses=statuses, lastUpdates=last_updates)
    elif step["do"] == "download":
        _, _, body = view(step["number"])
        files = (body or {}).get("proofOfDelivery", {}).get("files", [])
        if files:
            status, headers, content = edge("brouwer", "/transport-orders/%s/proof-of-delivery/files/%s" % (step["number"], files[0]["fileId"]))
            seen.update(httpStatus=status, contentType=headers.get("content-type"), bytes=len(content), correlationId=headers.get("x-correlation-id"))
        else:
            seen.update(httpStatus=None)
    elif step["do"] == "send":
        order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
        shipment = tms("/v1/shipments/" + order["shipmentNumber"]) if order.get("shipmentNumber") else {}
        confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
        seen["carrierId"] = (shipment.get("carriedBy", {}).get("carrier") or {}).get("id")
        if confirmed:
            # Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past.
            when = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2) + timedelta(minutes=step["offset"])
            event = {"bookingRef": confirmed[0]["carrierBookingReference"], "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["code"],
                     "eventTime": when.isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
            status, headers, _ = call("POST", os.environ["PARTNER_API"] + "/carrier-tracking", {
                "Authorization": "Bearer " + os.environ["VAN_DIJK_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["VAN_DIJK_KEY"],
                "Content-Type": "application/json"}, event)
            seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"))
    results.append(seen)
print("SMOKE-RESULT " + json.dumps(results))
PYTHON
'@
function Invoke-Steps($Steps, [string] $Number) {
    $result = Invoke-ThrowawayJobForResult -Prefix smoke-shipper-visibility -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 900 `
        -Purpose "the job's calls" `
        -IdentityId $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID, $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
        -Secrets ([ordered]@{ BROUWER_KEY = $brouwerKey; VAN_DIJK_KEY = $vanDijkKey }) -Environment ([ordered]@{
            TENANT                   = $tenant
            PARTNER_API              = $env:AZURE_ESTATE_PARTNER_API_URL
            PARTNER_API_CLIENT_ID    = $env:AZURE_ESTATE_PARTNER_API_CLIENT_ID
            PARTNER_PORTAL_CLIENT_ID = $env:AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID
            BROUWER_CLIENT_ID        = $brouwerClientId
            VAN_DIJK_CLIENT_ID       = $vanDijkClientId
            INTAKE_CLIENT_ID         = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
            TMS                      = $tms
            TMS_API_CLIENT_ID        = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
            NUMBER                   = $Number
            RUN                      = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
            STEPS                    = ConvertTo-Json -InputObject @($Steps) -Compress -Depth 5
        })
    # What the steps saw, by step name.
    $byName = @{}
    foreach ($step in @($result)) { $byName[$step.name] = $step }
    $byName
}

$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
Write-Host "Partner portal: $portal"
Write-Host "Story 14 Transport Order: $Story14"
Write-Host "Story 15 Transport Order: $Story15"

function ConvertTo-Brief($Value) { $Value | ConvertTo-Json -Compress -Depth 5 }

# Put back whatever API Management was calling, if the run ends while it is pointed away from the TMS.
$restoreTms = $false
try {
    # 1. Story 14.
    $step = Invoke-Steps @(@{ name = 'before'; do = 'get'; as = 'brouwer'; path = "/transport-orders/$Story14" }) $Story14
    if ($step.before.httpStatus -ne 200) { Write-Fail "Brouwer Dranken can't read Transport Order $Story14 through API Management: $(ConvertTo-Brief @{ httpStatus = $step.before.httpStatus })" }
    if ($step.before.status -ne 'SCHEDULED') { Write-Fail "Transport Order $Story14 isn't SCHEDULED (Planned, assigned to Kessler Spedition, not released): $(ConvertTo-Brief @{ status = $step.before.status })" }
    Write-Pass "Brouwer Dranken sees Transport Order $Story14 as SCHEDULED"
    Write-Host ''
    Write-Host "Open Brouwer Dranken's tracking screen and the Transport Order, and keep it open:"
    Write-Host "  $portal/personas/brouwer-dranken/tracking/$Story14"
    Write-Host 'Then release its Shipment in the TMS UI (Kessler Spedition confirms and progresses it by itself):'
    Write-Host "  $tmsUi"
    Read-Host 'Press Enter once you released it... ' | Out-Null
    $step = Invoke-Steps @(
        @{ name = 'watch'; do = 'watch'; number = $Story14; seconds = 600; until = 'DELIVERED' }
        @{ name = 'download'; do = 'download'; number = $Story14 }) $Story14
    $watch = $step.watch
    $statuses = @($watch.statuses)
    if (-not ($statuses.Count -gt 0 -and $statuses[-1] -eq 'DELIVERED')) {
        Write-Fail "Transport Order $Story14 didn't reach DELIVERED in the Shipper's view in 10 minutes: $(ConvertTo-Brief @{ statuses = $statuses })"
    }
    $order = 'SCHEDULED', 'COLLECTED', 'IN_TRANSIT', 'DELIVERED'
    $positions = @($statuses | ForEach-Object { [array]::IndexOf($order, $_) })
    if ($positions -contains -1 -or (($positions -join ',') -ne ((@($positions) | Sort-Object) -join ','))) {
        Write-Fail "the Shipper's view of Transport Order $Story14 moved backwards or showed an unknown status: $(ConvertTo-Brief @{ statuses = $statuses })"
    }
    Write-Pass "the Shipper's view moved forward only, $($statuses -join ' > ')"
    if (-not (@($watch.files).Count -gt 0 -and @($watch.codes)[-1] -eq 'DELIVERED')) {
        Write-Fail "the DELIVERED view of $Story14 has no Proof of delivery file, or no Delivered event: $(ConvertTo-Brief @{ files = $watch.files; codes = $watch.codes })"
    }
    $download = $step.download
    if (-not ($download.httpStatus -eq 200 -and $download.bytes -gt 0)) {
        Write-Fail "the sample CMR didn't download through API Management: $(ConvertTo-Brief @{ httpStatus = $download.httpStatus; contentType = $download.contentType; bytes = $download.bytes })"
    }
    Write-Pass "the sample CMR downloads through API Management ($($download.contentType), $($download.bytes) bytes)"
    $detailId = $watch.correlationId
    if (-not (Read-Confirmation 'Did the screen move from Scheduled to Delivered by itself, and offer the sample CMR for download?')) {
        Write-Fail "the presenter didn't see Transport Order $Story14's screen move by itself to Delivered with the sample CMR"
    }
    Write-Pass "the presenter saw Brouwer Dranken's screen move from Scheduled to Delivered by itself and offer the sample CMR"

    # 2. A Carrier Persona is refused.
    $step = Invoke-Steps @(
        @{ name = 'carrier-list'; do = 'get'; as = 'vandijk'; path = '/transport-orders' }
        @{ name = 'carrier-detail'; do = 'get'; as = 'vandijk'; path = "/transport-orders/$Story14" }) $Story14
    foreach ($name in 'carrier-list', 'carrier-detail') {
        if ($step[$name].httpStatus -ne 403) { Write-Fail "a Carrier Persona's token against the tracking API ($name) didn't get 403: $(ConvertTo-Brief @{ httpStatus = $step[$name].httpStatus })" }
    }
    $refusedId = $step['carrier-list'].correlationId
    Write-Pass "Van Dijk Transport's token against the tracking API gets 403 Forbidden (correlation ID $refusedId)"

    # 3. Story 15.
    $step = Invoke-Steps @(
        @{ name = 'before'; do = 'get'; as = 'brouwer'; path = "/transport-orders/$Story15" }
        @{ name = 'PU'; do = 'send'; code = 'PU'; offset = 0 }
        @{ name = 'DEP'; do = 'send'; code = 'DEP'; offset = 10 }
        @{ name = 'in-transit'; do = 'watch'; number = $Story15; seconds = 300; until = 'IN_TRANSIT' }) $Story15
    if ($step.before.status -ne 'SCHEDULED') { Write-Fail "Transport Order $Story15 isn't SCHEDULED (Booked, not tracked yet): $(ConvertTo-Brief @{ httpStatus = $step.before.httpStatus; status = $step.before.status })" }
    if ($step.PU.carrierId -ne $vanDijkId) { Write-Fail "Transport Order $Story15 isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(ConvertTo-Brief @{ carrierId = $step.PU.carrierId; httpStatus = $step.PU.httpStatus })" }
    foreach ($code in 'PU', 'DEP') {
        if ($step[$code].httpStatus -ne 202) { Write-Fail "Van Dijk Transport's $code didn't get 202 Accepted: $(ConvertTo-Brief @{ httpStatus = $step[$code].httpStatus })" }
    }
    if (@($step['in-transit'].statuses)[-1] -ne 'IN_TRANSIT') { Write-Fail "the Shipper doesn't see Transport Order $Story15 IN_TRANSIT after PU and DEP: $(ConvertTo-Brief @{ statuses = $step['in-transit'].statuses })" }
    Write-Pass "PU and DEP returned 202 Accepted and Brouwer Dranken sees Transport Order $Story15 as IN_TRANSIT"
    Write-Host ''
    Write-Host "Open Brouwer Dranken's tracking screen for Transport Order ${Story15}: it says In transit."
    Write-Host "  $portal/personas/brouwer-dranken/tracking/$Story15"
    Read-Host 'Press Enter when it is open... ' | Out-Null
    # DLV is outside the mapping table, so the Carrier-tracking Logic App's run fails, as P5's presenter-panel fault makes it.
    $step = Invoke-Steps @(
        @{ name = 'before'; do = 'watch'; number = $Story15; seconds = 1; until = $null }
        @{ name = 'DLV'; do = 'send'; code = 'DLV'; offset = 20 }
        @{ name = 'after'; do = 'watch'; number = $Story15; seconds = 90; until = $null }) $Story15
    if ($step.DLV.httpStatus -ne 202) { Write-Fail "the Carrier's unmapped DLV didn't get 202 Accepted: $(ConvertTo-Brief @{ httpStatus = $step.DLV.httpStatus })" }
    Write-Pass "the Carrier's DLV, a code outside the mapping table, returned 202 Accepted"
    $after = $step.after
    if (-not ((Test-SameValues $after.statuses 'IN_TRANSIT') -and @($after.lastUpdates).Count -eq 1 -and $null -ne @($after.lastUpdates)[0])) {
        Write-Fail "the Shipper's view of $Story15 changed after the unmapped DLV: $(ConvertTo-Brief @{ statuses = $after.statuses; lastUpdates = $after.lastUpdates })"
    }
    if (-not (Test-SameValues $after.lastUpdates $step.before.lastUpdates)) { Write-Fail "the last update of $Story15 moved after the unmapped DLV: $(ConvertTo-Brief @{ lastUpdates = $after.lastUpdates })" }
    Write-Pass "90 seconds after the DLV the Shipper still sees IN_TRANSIT, last update $(@($after.lastUpdates)[0]) (unchanged)"
    if (-not (Read-Confirmation 'Does the screen stay on In transit, with its last update getting older?')) {
        Write-Fail "the presenter didn't see Transport Order $Story15's screen stay on In transit with an ageing last update"
    }
    Write-Pass 'the presenter saw the screen stay on In transit with its last update getting older'
    $stuckId = $after.correlationId

    # 4. With the TMS stopped for API Management, the query fails at once, with a correlation ID.
    Write-Host ''
    Write-Host "Pointing API Management's tms-api backend away from the TMS..."
    $restoreTms = $true
    Set-Backend 'https://tms-api.stopped.invalid'
    Start-Sleep -Seconds 60
    $step = Invoke-Steps @(@{ name = 'stopped'; do = 'get'; as = 'brouwer'; path = "/transport-orders/$Story15" }) $Story15
    if ($step.stopped.httpStatus -ne 503) { Write-Fail "a query with the TMS stopped didn't get 503: $(ConvertTo-Brief @{ httpStatus = $step.stopped.httpStatus })" }
    $stoppedId = $step.stopped.correlationId
    if (-not $stoppedId) { Write-Fail "API Management's 503 carries no correlation ID" }
    Write-Pass "a query with the TMS stopped returns 503 with correlation ID $stoppedId"
    Write-Host "Look at Brouwer Dranken's screen (it polls every few seconds): it shows the error, and when its last answer was fetched."
    if (-not (Read-Confirmation 'Does the screen keep its last answer, show the error and show the time it was fetched?')) {
        Write-Fail "the presenter didn't see the error with the time of the last answer on Transport Order $Story15's screen"
    }
    Write-Pass 'the presenter saw the error with the time of the last answer'
    Set-Backend $backendUrl
    $restoreTms = $false
    Start-Sleep -Seconds 60
    $step = Invoke-Steps @(@{ name = 'back'; do = 'get'; as = 'brouwer'; path = "/transport-orders/$Story15" }) $Story15
    if ($step.back.httpStatus -ne 200) { Write-Fail "the query doesn't work again after the TMS is back: $(ConvertTo-Brief @{ httpStatus = $step.back.httpStatus })" }
    Write-Pass 'the query works again once the TMS is back'

    # 5. Each query by its correlation ID. Azure Monitor names the TMS after its Container Apps environment too.
    $apim = $env:AZURE_ESTATE_API_MANAGEMENT
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the Transport Order query, by correlation ID $detailId," -Value $detailId -Roles $apim, 'tms-api'
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the Carrier's refused query, by correlation ID $refusedId," -Value $refusedId -Roles $apim
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the query of the stuck Transport Order, by correlation ID $stuckId," -Value $stuckId -Roles $apim, 'tms-api'
    Assert-InApplicationInsights -WorkspaceId $workspace -What "the failed query with the TMS stopped, by correlation ID $stoppedId," -Value $stoppedId -Roles $apim

    $console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
    Write-Host ''
    Write-Host 'Open in the Partner portal:'
    Write-Host "  Story 14: $portal/personas/brouwer-dranken/tracking/$Story14"
    Write-Host "  Story 15: $portal/personas/brouwer-dranken/tracking/$Story15"
    Write-Host 'Open in the Integration console, by each correlation ID (the unmapped DLV is on the carrier-tracking dead-letter queue; discard it there afterwards):'
    Write-Host "  Transport Order query: $console/?key=$detailId"
    Write-Host "  Refused query: $console/?key=$refusedId"
    Write-Host "  Stuck Transport Order query: $console/?key=$stuckId"
    Write-Host "  Query with the TMS stopped: $console/?key=$stoppedId"
}
finally {
    if ($restoreTms) {
        try { Set-Backend $backendUrl }
        catch { Write-Host "Put API Management's tms-api backend back to $backendUrl" }
    }
}
