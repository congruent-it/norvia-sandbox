#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy demo smoke run of the Norvia Partner API (P1.10, not in CI).
.DESCRIPTION
    Post-deploy demo smoke run of the Norvia Partner API (P1.10, not in CI). Run it after `azd up`, signed in to the Azure
    CLI as the presenter the estate was provisioned for. It books through API Management and checks that:
      1. Brouwer Dranken, with its token and subscription key, gets a Transport Order number, and the Transport Order is
         readable in the TMS, for Brouwer Dranken even though the booking and its headers name another Shipper;
      2. Van Dijk Transport, a Carrier without the submit role, gets 403, and no intake Logic App run starts;
      3. a call without a subscription key, with a wrong one, without a token or with a token for another API is refused;
      4. the correlation ID API Management created is the one on the Logic App run, the Transport Order and the
         TransportOrderAccepted event, whatever correlation ID the caller sent;
      5. the accepted and the refused bookings are in Application Insights by correlation ID, and the accepted one also by
         its Transport Order number.
    A Persona's token can be had only as the Partner portal's identity (ADR 0005), so a throwaway Container Apps job runs
    the Personas' calls as it, and reads the Transport Order back as the intake identity. It prints what it saw, which
    this script reads from Log Analytics; it never prints a token or a key.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$arm = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers"
$partnerApi = $env:AZURE_ESTATE_PARTNER_API_URL
$namespace = $env:AZURE_ESTATE_SERVICE_BUS_NAMESPACE
$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
function Get-SubscriptionKey([string] $Name) {
    (Invoke-Az rest --method post --url "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/$Name/listSecrets?api-version=2024-05-01" `
            --query primaryKey --output tsv | Out-String).Trim()
}
$brouwerKey = Get-SubscriptionKey brouwer-dranken
$vanDijkKey = Get-SubscriptionKey van-dijk-transport

# The booking names another active Shipper in its body and headers, and sends a correlation ID of its own. API
# Management must ignore all three.
$bookingReference = "SMOKE-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
$bookingObject = Get-Content -Raw (Join-Path $PSScriptRoot '../contracts/partner-api/samples/bookings/minimal.booking.json') | ConvertFrom-Json -AsHashtable
$bookingObject.bookingRef = $bookingReference
$bookingObject.shipperId = 'SHP-0003'
$booking = $bookingObject | ConvertTo-Json -Depth 20 -Compress

Write-Host "Norvia Partner API: $partnerApi"

# 3. Refused for the key or the token. These need no Persona token, so they run from here.
$refusedForCredentials = @()
function Assert-Refused([string] $What, [hashtable] $Headers) {
    $response = Invoke-WebRequest -Method Post -Uri "$partnerApi/transport-orders" -Headers $Headers -ContentType 'application/json' -Body $booking -SkipHttpErrorCheck
    $correlationId = @($response.Headers['X-Correlation-ID'])[0]
    if ($response.StatusCode -ne 401) { Write-Fail "${What}: API Management answered $($response.StatusCode), not 401" }
    if (-not $correlationId) { Write-Fail "${What}: the refusal carries no correlation ID" }
    $script:refusedForCredentials += $correlationId
    Write-Pass "$What is refused (401, correlation ID $correlationId)"
}
Assert-Refused 'a call without a subscription key' @{ Authorization = 'Bearer not-a-token' }
Assert-Refused 'a call with a wrong subscription key' @{ 'Ocp-Apim-Subscription-Key' = '00000000000000000000000000000000' }
Assert-Refused 'a call without a token' @{ 'Ocp-Apim-Subscription-Key' = $brouwerKey }
Assert-Refused 'a call with a token for another API' @{ 'Ocp-Apim-Subscription-Key' = $brouwerKey; Authorization = "Bearer $(Get-AzAccessToken 'https://management.azure.com')" }

# The test's own subscription, created before the booking, sees the Shipper's events. Its filter keeps to the tracking
# contract (coding guidelines §7). Service Bus deletes it after 10 idle minutes if the cleanup below doesn't run.
$run = Get-ThrowawayJobName 'smoke-partner-api'
$subscriptionName = $run
$subscriptionArguments = @('--resource-group', $resourceGroup, '--namespace-name', $namespace, '--topic-name', 'transport-order-events')
try {
    Invoke-Az servicebus topic subscription create @subscriptionArguments --name $subscriptionName --auto-delete-on-idle PT10M --output none | Out-Null
    Invoke-Az servicebus topic subscription rule create @subscriptionArguments --subscription-name $subscriptionName --name brouwer-dranken `
        --filter-sql-expression "(partyId = 'SHP-0001' AND resubmitTo IS NULL) OR resubmitTo = '$subscriptionName'" --output none | Out-Null
    Invoke-Az servicebus topic subscription rule delete @subscriptionArguments --subscription-name $subscriptionName --name '$Default' --output none | Out-Null

    # 1 and 2. The Personas' calls, as the Partner portal's identity.
    $calls = @'
set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
persona_token() {
  curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
    -d grant_type=client_credentials -d client_id="$1" -d scope="$PARTNER_API_CLIENT_ID/.default" \
    -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
    | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])"
}
submit() {
  curl -sS -o "/tmp/$1.json" -D "/tmp/$1.headers" -w "%{http_code}" -X POST "$PARTNER_API/transport-orders" \
    -H "Authorization: Bearer $2" -H "Ocp-Apim-Subscription-Key: $3" -H "Content-Type: application/json" \
    -H "X-Shipper-ID: SHP-0003" -H "X-Correlation-ID: sent-by-the-caller" -d "$BOOKING" > "/tmp/$1.status"
}
submit accepted "$(persona_token "$BROUWER_CLIENT_ID")" "$BROUWER_KEY"
submit refused "$(persona_token "$VAN_DIJK_CLIENT_ID")" "$VAN_DIJK_KEY"
number=$(python3 -c "import json; print(json.load(open(\"/tmp/accepted.json\")).get(\"norviaOrderNo\") or \"\")" 2>/dev/null || true)
: > /tmp/read.json
if [ -n "$number" ]; then
  az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
  token=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
  curl -fsS "$TMS/v1/transport-orders/$number" -H "Authorization: Bearer $token" > /tmp/read.json || true
fi
python3 - <<"PYTHON"
import json

def call(name):
    headers = {}
    for line in open(f"/tmp/{name}.headers"):
        key, _, value = line.partition(":")
        headers[key.strip().lower()] = value.strip()
    try:
        body = json.load(open(f"/tmp/{name}.json"))
    except ValueError:
        body = None
    return {"status": int(open(f"/tmp/{name}.status").read()), "correlationId": headers.get("x-correlation-id"), "body": body}

try:
    order = json.load(open("/tmp/read.json"))
    read = {"shipperId": order["shipper"]["id"], "correlationId": order["correlationId"], "number": order["transportOrderNumber"]}
except (ValueError, KeyError):
    read = None
# Only what the checks need: the Transport Order holds the Consignee's personal data.
print("SMOKE-RESULT " + json.dumps({"accepted": call("accepted"), "refused": call("refused"), "read": read}))
PYTHON
'@
    $result = Invoke-ThrowawayJobForResult -Prefix smoke-partner-api -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 600 `
        -Purpose "the Personas' calls" `
        -IdentityId $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID, $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
        -Secrets ([ordered]@{ BROUWER_KEY = $brouwerKey; VAN_DIJK_KEY = $vanDijkKey }) -Environment ([ordered]@{
            TENANT                   = $tenant
            PARTNER_API              = $partnerApi
            PARTNER_API_CLIENT_ID    = $env:AZURE_ESTATE_PARTNER_API_CLIENT_ID
            PARTNER_PORTAL_CLIENT_ID = $env:AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID
            BROUWER_CLIENT_ID        = Get-PersonaClientId brouwer-dranken
            VAN_DIJK_CLIENT_ID       = Get-PersonaClientId van-dijk-transport
            INTAKE_CLIENT_ID         = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
            TMS                      = $tms
            TMS_API_CLIENT_ID        = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
            BOOKING                  = $booking
        })
    $workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

    # 1. Accepted, and readable in the TMS for the Shipper from the token.
    $accepted = $result.accepted
    $acceptedId = $accepted.correlationId
    $number = $accepted.body.norviaOrderNo
    if (-not ($accepted.status -eq 201 -and $accepted.body.result -eq 'ACCEPTED')) { Write-Fail "Brouwer Dranken's booking wasn't accepted: $($accepted | ConvertTo-Json -Compress -Depth 5)" }
    Write-Pass "Brouwer Dranken's booking is accepted as Transport Order $number (correlation ID $acceptedId)"
    if ($result.read.number -ne $number) { Write-Fail "Transport Order $number isn't readable in the TMS" }
    if ($result.read.shipperId -ne 'SHP-0001') { Write-Fail "Transport Order $number is for $($result.read.shipperId), not Brouwer Dranken (SHP-0001) from the token" }
    Write-Pass "Transport Order $number is readable in the TMS, for SHP-0001 from the token, not SHP-0003 from the booking and its headers"

    # 4. One correlation ID, created by API Management.
    if (-not $acceptedId -or $acceptedId -eq 'sent-by-the-caller') { Write-Fail "API Management passed on the caller's correlation ID" }
    if (-not ($accepted.body.messageId -eq $acceptedId -and $result.read.correlationId -eq $acceptedId)) {
        Write-Fail "the answer or the Transport Order carries another correlation ID than $acceptedId"
    }
    Write-Pass 'the answer and the Transport Order carry the correlation ID API Management created'

    function Get-RunCount([string] $CorrelationId) {
        $runs = Invoke-AzRest -Method get -Url "$arm/Microsoft.Web/sites/$($env:AZURE_ESTATE_INTAKE_LOGIC_APP)/hostruntime/runtime/webhooks/workflow/api/management/workflows/transport-order-intake/runs?api-version=2018-11-01&`$filter=ClientTrackingId%20eq%20'$CorrelationId'"
        @($runs.value).Count
    }
    if ((Get-RunCount $acceptedId) -ne 1) { Write-Fail "no intake Logic App run has the client tracking ID $acceptedId" }
    Write-Pass 'the intake Logic App run has the correlation ID as its client tracking ID'

    $serviceBusToken = Get-AzAccessToken https://servicebus.azure.net
    $messages = "https://$namespace.servicebus.windows.net/transport-order-events/subscriptions/$subscriptionName/messages/head?timeout=60"
    $eventFound = $false
    for ($i = 0; $i -lt 5 -and -not $eventFound; $i++) {
        $response = Invoke-WebRequest -Method Delete -Uri $messages -Headers @{ Authorization = "Bearer $serviceBusToken" } -SkipHttpErrorCheck
        $brokerProperties = @($response.Headers['BrokerProperties'])[0]
        if (-not $brokerProperties) { break }
        $properties = $brokerProperties | ConvertFrom-Json
        $eventFound = $properties.Label -eq 'TransportOrderAccepted' -and $properties.CorrelationId -eq $acceptedId
    }
    if (-not $eventFound) { Write-Fail "no TransportOrderAccepted event with correlation ID $acceptedId reached transport-order-events" }
    Write-Pass 'the TransportOrderAccepted event carries the correlation ID'

    # 2. Refused at the edge.
    $refused = $result.refused
    $refusedId = $refused.correlationId
    if ($refused.status -ne 403) { Write-Fail "Van Dijk Transport's booking got $($refused.status), not 403" }
    if (-not $refusedId) { Write-Fail "Van Dijk Transport's refusal carries no correlation ID" }
    Write-Pass "Van Dijk Transport's booking is refused with 403 (correlation ID $refusedId)"
    if ((Get-RunCount $refusedId) -ne 0) { Write-Fail "an intake Logic App run started for Van Dijk Transport's refused booking" }
    Write-Pass "no intake Logic App run started for Van Dijk Transport's booking"

    # 5. Application Insights. API Management and Logic Apps name themselves after their resource; the TMS after its
    # Container Apps environment too: [<environment>]/tms-api.
    $recent = "TimeGenerated > ago(1h) and tostring(Properties) contains '{0}'"
    $apim = $env:AZURE_ESTATE_API_MANAGEMENT
    $intakeApp = $env:AZURE_ESTATE_INTAKE_LOGIC_APP
    Assert-InApplicationInsights -WorkspaceId $workspace -What 'the accepted booking, by correlation ID,' -Value $acceptedId -Roles $apim, $intakeApp, 'tms-api' -Filter $recent
    Assert-InApplicationInsights -WorkspaceId $workspace -What 'the accepted booking, by Transport Order number,' -Value $number -Roles $intakeApp -Filter $recent
    Assert-InApplicationInsights -WorkspaceId $workspace -What "Van Dijk Transport's refused booking, by correlation ID," -Value $refusedId -Roles $apim -Filter $recent
    $roles = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "union AppRequests, AppTraces, AppDependencies | where TimeGenerated > ago(1h) and tostring(Properties) contains '$refusedId' | summarize by AppRoleName"
    if (@($roles | Where-Object { $_[0] -like "*$intakeApp*" }).Count -gt 0) { Write-Fail "the intake Logic App traced Van Dijk Transport's refused booking" }
    foreach ($correlationId in $refusedForCredentials) {
        Assert-InApplicationInsights -WorkspaceId $workspace -What "the call refused for its key or token ($correlationId)" -Value $correlationId -Roles $apim -Filter $recent
    }
}
finally {
    Invoke-Az servicebus topic subscription delete @subscriptionArguments --name $subscriptionName --output none -AllowFailure | Out-Null
}
