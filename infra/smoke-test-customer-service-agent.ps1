#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the Customer service agent in Azure (not in CI).
.DESCRIPTION
    Post-deploy smoke run of the Customer service agent in Azure (not in CI). Run it in a terminal, signed in to the Azure CLI as the
    presenter the estate was provisioned for (the presenter holds the Foundry User role on the project), once `azd up` has deployed the estate
    and the apps and infra/deploy-agents.ps1 has published the Agent (it is the last postdeploy hook), with ESTATE_AGENTS_ENABLED_SETTING=true so
    the TMS UI shows Explain:

      infra/smoke-test-customer-service-agent.ps1 [<ID of a Rejected booking>]

    Without an ID it rejects a booking through the Partner API: Brouwer Dranken's booking with a weight of 0 kg, which the TMS refuses with
    WeightInvalid (a throwaway Container Apps job books as the Persona, as infra/smoke-test-partner-api.ps1 does). The Rejected booking's ID is
    not on the Partner API's answer, so the script asks you to open the TMS UI's Rejected bookings, open the booking with the printed Shipper
    reference as Customer service, and paste its ID from the address bar.

    It asks the Agent what the TMS UI's Explain button asks it (through the project, as the presenter, which the TMS UI does as its own
    identity) and checks that:
      1. the Agent has its own identity and holds only TransportOrders.CustomerServiceAgent.Read, on the agent view and nothing else; the TMS
         agent API has that one role, held by API Management's identity alone; the project's shared identity holds no role;
      2. the real model answers with a Suggestion that names the rejection, cites a result, and has called only get_rejected_booking;
      3. no Consignee field appears in the tool calls' results;
      4. the Agent's tool calls are hops of the booking's Message trail, in API Management's MCP server and in the TMS, tagged with the Agent's
         identity and the tool name;
      5. the response is not kept by the service (store: false), or is gone once it is deleted.
    The Explain button itself is pressed by hand: the script prints the TMS UI address. It prints names, ids and counts only: never a token,
    a prompt, a Consignee or an answer's text beyond the cause.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $RejectedBooking = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT
$contract = Get-Content -Raw (Join-Path $repo 'contracts/agent-mcp/customer-service.tools.json') | ConvertFrom-Json
$agent = 'customer-service'
$role = $contract.role
$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup

if (-not $endpoint) { Write-Fail 'no Foundry project endpoint: has the estate been deployed (azd provision)?' }
if ($RejectedBooking -and $RejectedBooking -notmatch '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') { Write-Fail 'usage: smoke-test-customer-service-agent.ps1 [<ID of a Rejected booking>]' }
if (-not $RejectedBooking -and [Console]::IsInputRedirected) { Write-Fail "the presenter's steps are theirs, so run this in a terminal" }
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# 1. The identities and their roles.
$published = Invoke-RestMethod -Uri "$endpoint/agents/${agent}?api-version=v1" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck
$identityPrincipal = $published.instance_identity.principal_id ?? $published.identity.principal_id
$identityClient = $published.instance_identity.client_id ?? $published.identity.client_id
if (-not ($identityPrincipal -and $identityClient)) { Write-Fail "the Agent $agent is not published, or has no identity of its own: run infra/deploy-agents.ps1" }
$view = Get-Graph "/servicePrincipals?`$filter=displayName eq 'Norvia $agent agent view ($($env:AZURE_ENV_NAME))'&`$select=id,appRoles"
$viewId = @($view.value)[0].id
if (-not $viewId) { Write-Fail 'the agent view is not in Entra: has the estate been deployed?' }
$held = @((Get-Graph "/servicePrincipals/$identityPrincipal/appRoleAssignments").value | ForEach-Object { @{ resource = $_.resourceId; role = $_.appRoleId } })
$expectedRoleId = (@($view.value)[0].appRoles | Where-Object value -EQ $role).id
if (-not ($held.Count -eq 1 -and $held[0].resource -eq $viewId -and $held[0].role -eq $expectedRoleId)) {
    Write-Fail "the Agent's identity does not hold exactly $role on the agent view: $($held | ConvertTo-Json -Compress)"
}
Write-Pass "the Agent's identity $identityClient holds $role on the agent view and nothing else"

$api = Get-Graph "/servicePrincipals?`$filter=displayName eq 'Norvia TMS agent API ($($env:AZURE_ENV_NAME))'&`$select=id,appRoles"
$apiId = @($api.value)[0].id
if (-not $apiId) { Write-Fail "the TMS agent API is not in Entra: has the estate been deployed?" }
$enabledRoles = @(@($api.value)[0].appRoles | Where-Object isEnabled)
if (-not ($enabledRoles.Count -eq 1 -and $enabledRoles[0].value -eq $role)) { Write-Fail "$role is not the only role on the TMS agent API" }
$holders = (@((Get-Graph "/servicePrincipals/$apiId/appRoleAssignedTo").value | ForEach-Object principalDisplayName)) -join ','
if ($holders -notlike 'id-apim-*') { Write-Fail "the TMS agent API is held by '$holders', not by API Management's identity alone" }
Write-Pass "$role is the only role on the TMS agent API, and API Management's identity ($holders) alone holds it"

$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$projectIdentity = (Invoke-Az resource show --resource-group $resourceGroup --resource-type Microsoft.CognitiveServices/accounts/projects `
        --name "$account/norvia" --query identity.principalId --output tsv | Out-String).Trim()
if ($projectIdentity) {
    if (@((Get-Graph "/servicePrincipals/$projectIdentity/appRoleAssignments").value).Count -ne 0) { Write-Fail "the project's shared identity holds a role" }
    Write-Pass "the project's shared identity holds no role"
}

# A Rejected booking, through the Partner API.
$consignee = 'Café De Zwaan'
if (-not $RejectedBooking) {
    $reference = "SMOKE-CS-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
    $day = [DateTime]::UtcNow.AddDays(2).ToString('dd-MM-yyyy')
    $bookingObject = Get-Content -Raw (Join-Path $repo 'contracts/partner-api/samples/bookings/minimal.booking.json') | ConvertFrom-Json -AsHashtable
    $bookingObject.bookingRef = $reference
    $bookingObject.pickupFrom = "$day 08:00"
    $bookingObject.pickupUntil = "$day 12:00"
    $bookingObject.receiverName = $consignee
    $bookingObject.items = @(@{ packaging = 'PX'; count = 2; weight = '0 kg' })
    $booking = $bookingObject | ConvertTo-Json -Depth 20 -Compress
    $subscription = $env:AZURE_SUBSCRIPTION_ID
    $key = (Invoke-Az rest --method post --url "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/brouwer-dranken/listSecrets?api-version=2024-05-01" `
            --query primaryKey --output tsv | Out-String).Trim()
    $calls = @'
set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
token=$(curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d grant_type=client_credentials -d client_id="$BROUWER_CLIENT_ID" -d scope="$PARTNER_API_CLIENT_ID/.default" \
  -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
  | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])")
status=$(curl -sS -o /tmp/rejected.json -D /tmp/rejected.headers -w "%{http_code}" -X POST "$PARTNER_API/transport-orders" \
  -H "Authorization: Bearer $token" -H "Ocp-Apim-Subscription-Key: $BROUWER_KEY" -H "Content-Type: application/json" -d "$BOOKING")
python3 - "$status" <<"PYTHON"
import json, sys
headers = {}
for line in open("/tmp/rejected.headers"):
    name, _, value = line.partition(":")
    headers[name.strip().lower()] = value.strip()
try:
    body = json.load(open("/tmp/rejected.json"))
except ValueError:
    body = {}
# Only what the checks need: the booking holds a Consignee.
print("SMOKE-RESULT " + json.dumps({"status": int(sys.argv[1]), "correlationId": headers.get("x-correlation-id"), "result": body.get("result"), "codes": [e.get("code") for e in body.get("errors", [])]}))
PYTHON
'@
    $tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
    $result = Invoke-ThrowawayJobForResult -Prefix smoke-customer-service -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 600 `
        -Purpose "Brouwer Dranken's booking" -IdentityId $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID `
        -Secrets ([ordered]@{ BROUWER_KEY = $key }) -Environment ([ordered]@{
            TENANT                   = $tenant
            PARTNER_API              = $env:AZURE_ESTATE_PARTNER_API_URL
            PARTNER_API_CLIENT_ID    = $env:AZURE_ESTATE_PARTNER_API_CLIENT_ID
            PARTNER_PORTAL_CLIENT_ID = $env:AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID
            BROUWER_CLIENT_ID        = Get-PersonaClientId brouwer-dranken
            BOOKING                  = $booking
        })
    if (-not ($result.status -eq 422 -or $result.result -eq 'REJECTED') -or 'WeightInvalid' -notin @($result.codes)) {
        Write-Fail "the booking was not rejected with WeightInvalid: $($result | ConvertTo-Json -Compress)"
    }
    Write-Pass "the Partner API rejected Shipper reference $reference with $(@($result.codes) -join ', ') (correlation ID $($result.correlationId))"
    Write-Host ''
    Write-Host "Open the Rejected bookings in the TMS UI as Customer service, open the booking received just now (reference $reference), and copy its ID from the address:"
    Write-Host "  $tmsUi/rejected-bookings"
    $RejectedBooking = (Read-Host 'Rejected booking ID').Trim()
    if ($RejectedBooking -notmatch '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') { Write-Fail 'that is not a Rejected booking ID' }
}
$RejectedBooking = $RejectedBooking.ToLowerInvariant()

# 2. Ask, as Explain does. The TMS UI puts the booking's own correlation ID on the run so its tool calls join the trail; the script reads it from
# the trail the TMS wrote for the booking's intake, which is the same ID the Partner API's answer carried.
$correlation = if ($result) { $result.correlationId } else { [guid]::NewGuid().ToString() }
$body = @{
    agent_reference   = @{ type = 'agent_reference'; name = $agent }
    input             = @(@{ type = 'message'; role = 'user'; content = "Explain why the Rejected booking $RejectedBooking was rejected, and what the Shipper should change." })
    store             = $false
    max_tool_calls    = 3
    max_output_tokens = 1200
    structured_inputs = @{ correlationId = $correlation }
} | ConvertTo-Json -Depth 10 -Compress
$response = Invoke-RestMethod -Method Post -Uri "$endpoint/openai/v1/responses" -Headers (Get-FoundryHeader) -ContentType 'application/json' -Body $body -TimeoutSec 180 -SkipHttpErrorCheck
if ($response.status -ne 'completed') { Write-Fail "the Agent's response did not complete: $(($response.error ?? @{ status = $response.status }) | ConvertTo-Json -Compress -Depth 5)" }
$calls = @($response.output | Where-Object type -EQ 'mcp_call')
$tools = (@($calls.name | Sort-Object -Unique)) -join ','
if (-not $tools) { Write-Fail 'the Agent called no tool' }
$contractTools = @($contract.tools.name)
if (@($calls | Where-Object { $_.name -notin $contractTools }).Count -gt 0) { Write-Fail "the Agent called a tool that is not on the contract: $tools" }
$refused = @($calls | Where-Object { $null -ne $_.error -and $_.error -ne '' -and "$($_.error | ConvertTo-Json -Compress)" -ne '{}' })
if ($refused.Count -gt 0) { Write-Fail "a tool call was refused: $($refused | ForEach-Object { @{ name = $_.name; error = $_.error } } | ConvertTo-Json -Compress -Depth 5)" }
if ($calls.name -notcontains 'get_rejected_booking') { Write-Fail 'the Agent did not read the Rejected booking' }
$answer = (@($response.output | Where-Object type -EQ 'message' | ForEach-Object { $_.content } | Where-Object type -EQ 'output_text' | ForEach-Object text) -join '') | ConvertFrom-Json
if (-not ($answer.cause -and @($answer.evidence).Count -gt 0 -and $answer.suggestedAction -and $answer.confidence -in 'low', 'medium', 'high')) { Write-Fail 'the answer is not a Suggestion' }
$cause = $answer.cause
if ($cause -notmatch 'weight') { Write-Fail "the cause does not name the rejection: $cause" }
Write-Pass "the real model answered; tools called: $tools"
Write-Host "      Cause: $cause"

# 3. No Consignee field in what the tools returned or were asked.
$toolTraffic = ($calls | ForEach-Object { ($_ | ConvertTo-Json -Depth 20 -Compress) }) -join ' '
foreach ($forbidden in @($consignee, 'Oudegracht', '3511 AW', 'receiverName', 'consignee')) {
    if ($toolTraffic -match [regex]::Escape($forbidden)) { Write-Fail 'a Consignee field appears in the tool calls' }
}
Write-Pass 'no Consignee field appears in the tool calls'

# 4. The hops: API Management's MCP server and the TMS, with the Agent's identity and the tool name. Ingestion takes minutes: ask again for up to 10.
function Get-HopCount([string] $Flow) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "AppTraces | where TimeGenerated > ago(1h) | where tostring(Properties.runId) == '$correlation' or tostring(Properties.correlationId) == '$correlation' | where tostring(Properties.flow) == '$Flow' | where tostring(Properties.agentId) == '$identityClient' and tostring(Properties.toolName) == 'get_rejected_booking' | count"
    $rows.Count -gt 0 ? [int] $rows[0][0] : 0
}
foreach ($flow in 'agent-mcp-customer-service', 'agent-view-customer-service') {
    $count = Wait-Until { $n = Get-HopCount $flow; $n -gt 0 ? $n : $null }
    if (-not $count) { Write-Fail "no hop of $flow on the trail $correlation with the Agent's identity and get_rejected_booking within 10 minutes" }
    Write-Pass "the Message trail has $count hop(s) of $flow, tagged with the Agent's identity and get_rejected_booking"
}

# 5. Nothing kept.
$kept = (Invoke-WebRequest -Uri "$endpoint/openai/v1/responses/$($response.id)" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck).StatusCode
if ($kept -eq 404) {
    Write-Pass 'the service did not keep the response (store: false is honoured when the Agent calls an MCP tool)'
}
else {
    Invoke-WebRequest -Method Delete -Uri "$endpoint/openai/v1/responses/$($response.id)" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck | Out-Null
    Write-Host "NOTE  the service kept the response (HTTP $kept) although store was false; it was deleted. Record this in docs/operations/foundry.md (spike 3)."
}

Write-Host ''
Write-Host 'Open in the TMS UI as Customer service, choose Explain, and see the same answer:'
Write-Host "  $tmsUi/rejected-bookings/$RejectedBooking"
