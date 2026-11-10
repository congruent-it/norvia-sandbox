#Requires -Version 7.4
<#
.SYNOPSIS
    Submits the Booking drafts of an evaluation run through the booking path and returns what the TMS answered (#462). On demand, never in CI.
.DESCRIPTION
    infra/run-agent-evaluation.ps1 booking-intake -Submit calls this. It takes a JSON file of { "<case id>": <the draft's booking> }, sends each booking
    to the Norvia Partner API as Brouwer Dranken (a Shipper Persona), exactly as the Partner portal sends a reviewed draft
    (POST /transport-orders), and writes one JSON object { "<case id>": { "status", "result", "codes" } } to the result file.

    A Persona's token can be had only as the Partner portal's identity (ADR 0005), so the calls are made by a throwaway Container Apps job, as in
    the smoke runs. The Shipper reference of each booking gets the suffix of this run, so a second run is not answered ALREADY_BOOKED or
    ShipperReferenceReused; a draft without a reference stays without one, because that is the answer being tested. Every booking has its collection
    day in the future (the samples use 2030).

    It sends real bookings to the estate's TMS: the Transport Orders it makes are the estate's own, in the Shipper's list. Run it on an estate that
    can have them. It prints case ids, results and rejection codes; never a booking, a token or a key.
.PARAMETER DraftsFile The JSON file of drafts.
.PARAMETER ResultFile The JSON file the answers are written to.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string] $DraftsFile, [Parameter(Mandatory)][string] $ResultFile)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
if (-not $env:AZURE_ESTATE_PARTNER_API_URL) { Write-Fail 'no Partner API address: has the estate been deployed (azd provision)?' }

$suffix = "-E$([DateTime]::UtcNow.ToString('yyMMddHHmmss'))"
$drafts = Get-Content -Raw $DraftsFile | ConvertFrom-Json -AsHashtable -Depth 50
foreach ($booking in $drafts.Values) {
    if ($booking.bookingRef -is [string] -and $booking.bookingRef.Length -gt 0) {
        $booking.bookingRef = $booking.bookingRef.Substring(0, [math]::Min($booking.bookingRef.Length, 100 - $suffix.Length)) + $suffix
    }
}
$key = (Invoke-Az rest --method post --url "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/brouwer-dranken/listSecrets?api-version=2024-05-01" `
        --query primaryKey --output tsv | Out-String).Trim()
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()

$calls = @'
set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
token=$(curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d grant_type=client_credentials -d client_id="$BROUWER_CLIENT_ID" -d scope="$PARTNER_API_CLIENT_ID/.default" \
  -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
  | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])")
printf '%s' "$DRAFTS" > /tmp/drafts.json
python3 - "$token" <<"PYTHON"
import json, os, subprocess, sys
token = sys.argv[1]
results = {}
for case, booking in json.load(open("/tmp/drafts.json")).items():
    json.dump(booking, open("/tmp/booking.json", "w"))
    status = subprocess.run(
        ["curl", "-sS", "-o", "/tmp/answer.json", "-w", "%{http_code}", "-X", "POST", os.environ["PARTNER_API"] + "/transport-orders",
         "-H", "Authorization: Bearer " + token, "-H", "Ocp-Apim-Subscription-Key: " + os.environ["BROUWER_KEY"],
         "-H", "Content-Type: application/json", "--data-binary", "@/tmp/booking.json"],
        capture_output=True, text=True).stdout
    try:
        body = json.load(open("/tmp/answer.json"))
    except ValueError:
        body = {}
    # Only the outcome: the booking and the answer hold a Consignee's data.
    results[case] = {"status": int(status or 0), "result": body.get("result"), "codes": [e.get("code") for e in body.get("errors", [])]}
print("SMOKE-RESULT " + json.dumps(results))
PYTHON
'@
$result = Invoke-ThrowawayJobForResult -Prefix evaluate-intake -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 900 `
    -Purpose "Brouwer Dranken's bookings" -IdentityId $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID `
    -Secrets ([ordered]@{ BROUWER_KEY = $key; DRAFTS = ($drafts | ConvertTo-Json -Depth 20 -Compress) }) -Environment ([ordered]@{
        TENANT                   = $tenant
        PARTNER_API              = $env:AZURE_ESTATE_PARTNER_API_URL
        PARTNER_API_CLIENT_ID    = $env:AZURE_ESTATE_PARTNER_API_CLIENT_ID
        PARTNER_PORTAL_CLIENT_ID = $env:AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID
        BROUWER_CLIENT_ID        = Get-PersonaClientId brouwer-dranken
    })
$result | ConvertTo-Json -Depth 10 -Compress | Set-Content -Path $ResultFile -Encoding utf8NoBOM
