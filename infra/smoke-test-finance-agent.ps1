#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the Carrier invoice matching agent in Azure (not in CI).
.DESCRIPTION
    Post-deploy smoke run of the Carrier invoice matching agent in Azure (not in CI). Run it in a terminal, signed in to the Azure CLI as the
    presenter the estate was provisioned for (the presenter holds the Foundry User role on the project), once `azd up` has deployed the estate
    and the apps and infra/deploy-agents.ps1 has published the Agent (it is the last postdeploy hook), with ESTATE_AGENTS_ENABLED_SETTING=true so
    the ERP UI shows Explain:

      infra/smoke-test-finance-agent.ps1 [-BookingReference <a Carrier booking reference of Van Dijk Transport>] [-SpotRemark <text>]

    It submits a Carrier invoice of Van Dijk Transport through the Partner API with a mangled Carrier booking reference (the reference
    given, or a Spot one if you name none, with a character swapped), as the Carrier Persona (a throwaway Container Apps job calls as the
    Partner portal's identity, as infra/smoke-test-partner-api.ps1 does). The line is Unknown booking. The script then asks the Agent what the
    ERP UI's Explain button asks it (through the project, as the presenter, which the ERP UI does as its own identity) and checks that:
      1. the Agent has its own identity and holds only CarrierInvoices.FinanceAgent.Read, on the agent view and nothing else; the ERP agent
         API has that one role, held by API Management's identity alone; the project's shared identity holds no role;
      2. the real model answers with a Suggestion that names the cause (no Carrier cost under the reference), cites a result, and has
         called only get_invoice_line;
      3. no Spot remark appears in the tool calls: no remark field, and not the text given as -SpotRemark;
      4. the Agent's tool calls are hops of the invoice's Message trail, in API Management's MCP server and in the ERP, tagged with the Agent's
         identity and the tool name;
      5. the response is not kept by the service (store: false), or is gone once it is deleted.
    Finance opens the line in the ERP UI and presses Explain by hand: the script prints the address. It prints names, ids and counts only:
    never a token, a prompt, a remark or an answer's text beyond the cause.
#>
[CmdletBinding()]
param(
    [Parameter()][string] $BookingReference = '',
    [Parameter()][string] $SpotRemark = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT
$contract = Get-Content -Raw (Join-Path $repo 'contracts/agent-mcp/finance-matching.tools.json') | ConvertFrom-Json
$agent = 'finance-matching'
$role = $contract.role
$erpUi = Get-AppUrl erp-web -ResourceGroup $resourceGroup
$carrierId = 'CAR-0001'

if (-not $endpoint) { Write-Fail 'no Foundry project endpoint: has the estate been deployed (azd provision)?' }
if ($BookingReference -and $BookingReference -notmatch '^[A-Za-z0-9-]+$') { Write-Fail 'usage: smoke-test-finance-agent.ps1 [-BookingReference <reference>] [-SpotRemark <text>]' }
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup
function Get-FoundryHeaders { @{ Authorization = "Bearer $(Get-AzAccessToken https://ai.azure.com)" } }
function Get-Graph([string] $Path) { Invoke-AzRest -Method get -Url "https://graph.microsoft.com/v1.0$Path" }

# 1. The identities and their roles.
$published = Invoke-RestMethod -Uri "$endpoint/agents/${agent}?api-version=v1" -Headers (Get-FoundryHeaders) -TimeoutSec 60 -SkipHttpErrorCheck
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

$api = Get-Graph "/servicePrincipals?`$filter=displayName eq 'Norvia ERP agent API ($($env:AZURE_ENV_NAME))'&`$select=id,appRoles"
$apiId = @($api.value)[0].id
if (-not $apiId) { Write-Fail 'the ERP agent API is not in Entra: has the estate been deployed?' }
$enabledRoles = @(@($api.value)[0].appRoles | Where-Object isEnabled)
if (-not ($enabledRoles.Count -eq 1 -and $enabledRoles[0].value -eq $role)) { Write-Fail "$role is not the only role on the ERP agent API" }
$holders = (@((Get-Graph "/servicePrincipals/$apiId/appRoleAssignedTo").value | ForEach-Object principalDisplayName)) -join ','
if ($holders -notlike 'id-apim-*') { Write-Fail "the ERP agent API is held by '$holders', not by API Management's identity alone" }
Write-Pass "$role is the only role on the ERP agent API, and API Management's identity ($holders) alone holds it"

$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$projectIdentity = (Invoke-Az resource show --resource-group $resourceGroup --resource-type Microsoft.CognitiveServices/accounts/projects `
        --name "$account/norvia" --query identity.principalId --output tsv | Out-String).Trim()
if ($projectIdentity) {
    if (@((Get-Graph "/servicePrincipals/$projectIdentity/appRoleAssignments").value).Count -ne 0) { Write-Fail "the project's shared identity holds a role" }
    Write-Pass "the project's shared identity holds no role"
}

# A Carrier invoice with a mangled reference, as Van Dijk Transport through the Partner API.
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$invoiceNumber = "SMOKE-FA-$stamp"
$reference = if ($BookingReference) { $BookingReference.Substring(0, $BookingReference.Length - 1) + 'X' } else { "TRX-MANGLED-$stamp" }
$invoiceObject = Get-Content -Raw (Join-Path $repo 'contracts/partner-api/samples/carrier-invoices/edited-amount.carrier-invoice.json') | ConvertFrom-Json -AsHashtable
$invoiceObject.invoiceNo = $invoiceNumber
$invoiceObject.invoiceDate = [DateTime]::UtcNow.ToString('dd-MM-yyyy')
$invoiceObject.lines = @(@{ bookingRef = $reference; amount = 95.00 })
$invoice = $invoiceObject | ConvertTo-Json -Depth 20 -Compress
$subscription = $env:AZURE_SUBSCRIPTION_ID
$key = (Invoke-Az rest --method post --url "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/van-dijk-transport/listSecrets?api-version=2024-05-01" `
        --query primaryKey --output tsv | Out-String).Trim()
$calls = @'
set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
token=$(curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
  -d grant_type=client_credentials -d client_id="$CARRIER_CLIENT_ID" -d scope="$PARTNER_API_CLIENT_ID/.default" \
  -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
  | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])")
status=$(curl -sS -o /tmp/invoice.json -D /tmp/invoice.headers -w "%{http_code}" -X POST "$PARTNER_API/carrier-invoices" \
  -H "Authorization: Bearer $token" -H "Ocp-Apim-Subscription-Key: $CARRIER_KEY" -H "Content-Type: application/json" -d "$INVOICE")
python3 - "$status" <<"PYTHON"
import json, sys
headers = {}
for line in open("/tmp/invoice.headers"):
    name, _, value = line.partition(":")
    headers[name.strip().lower()] = value.strip()
try:
    body = json.load(open("/tmp/invoice.json"))
except ValueError:
    body = {}
# Only what the checks need.
print("SMOKE-RESULT " + json.dumps({"status": int(sys.argv[1]), "correlationId": headers.get("x-correlation-id"), "lines": [line.get("result") for line in body.get("lines", [])]}))
PYTHON
'@
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
$result = Invoke-ThrowawayJobForResult -Prefix smoke-finance-agent -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 600 `
    -Purpose "Van Dijk Transport's Carrier invoice" -IdentityId $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID `
    -Secrets ([ordered]@{ CARRIER_KEY = $key }) -Environment ([ordered]@{
        TENANT                   = $tenant
        PARTNER_API              = $env:AZURE_ESTATE_PARTNER_API_URL
        PARTNER_API_CLIENT_ID    = $env:AZURE_ESTATE_PARTNER_API_CLIENT_ID
        PARTNER_PORTAL_CLIENT_ID = $env:AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID
        CARRIER_CLIENT_ID        = Get-PersonaClientId van-dijk-transport
        INVOICE                  = $invoice
    })
if ($result.status -notin 200, 201, 202 -or -not $result.correlationId) {
    Write-Fail "the Carrier invoice was not received: $($result | ConvertTo-Json -Compress)"
}
$correlation = $result.correlationId
Write-Pass "the Partner API received Carrier invoice $invoiceNumber with line $reference (correlation ID $correlation)"
if (-not (Wait-Until { Get-DocumentHops -WorkspaceId $workspace -Document $invoiceNumber -Role erp-api })) { Write-Fail "the ERP tracked nothing for invoice $invoiceNumber after 10 minutes" }
Write-Pass "the ERP tracked invoice $invoiceNumber; its line is To decide as an Unknown booking"

# 2. Ask, as Explain does. The ERP UI puts the invoice's own correlation ID on the run so its tool calls join the trail.
$question = "Explain why the line `"$reference`" of Carrier invoice `"$invoiceNumber`" of Carrier `"$carrierId`" did not match, and whether it may still resolve."
$body = @{
    agent_reference   = @{ type = 'agent_reference'; name = $agent }
    input             = @(@{ type = 'message'; role = 'user'; content = $question })
    store             = $false
    max_tool_calls    = 3
    max_output_tokens = 1200
    structured_inputs = @{ correlationId = $correlation }
} | ConvertTo-Json -Depth 10 -Compress
$response = Invoke-RestMethod -Method Post -Uri "$endpoint/openai/v1/responses" -Headers (Get-FoundryHeaders) -ContentType 'application/json' -Body $body -TimeoutSec 180 -SkipHttpErrorCheck
if ($response.status -ne 'completed') { Write-Fail "the Agent's response did not complete: $(($response.error ?? @{ status = $response.status }) | ConvertTo-Json -Compress -Depth 5)" }
$toolCalls = @($response.output | Where-Object type -EQ 'mcp_call')
$tools = (@($toolCalls.name | Sort-Object -Unique)) -join ','
if (-not $tools) { Write-Fail 'the Agent called no tool' }
$contractTools = @($contract.tools.name)
if (@($toolCalls | Where-Object { $_.name -notin $contractTools }).Count -gt 0) { Write-Fail "the Agent called a tool that is not on the contract: $tools" }
$refused = @($toolCalls | Where-Object { $null -ne $_.error -and $_.error -ne '' -and "$($_.error | ConvertTo-Json -Compress)" -ne '{}' })
if ($refused.Count -gt 0) { Write-Fail "a tool call was refused: $($refused | ForEach-Object { @{ name = $_.name; error = $_.error } } | ConvertTo-Json -Compress -Depth 5)" }
if ($toolCalls.name -notcontains 'get_invoice_line') { Write-Fail 'the Agent did not read the invoice line' }
$answer = (@($response.output | Where-Object type -EQ 'message' | ForEach-Object { $_.content } | Where-Object type -EQ 'output_text' | ForEach-Object text) -join '') | ConvertFrom-Json
if (-not ($answer.cause -and @($answer.evidence).Count -gt 0 -and $answer.suggestedAction -and $answer.confidence -in 'low', 'medium', 'high')) { Write-Fail 'the answer is not a Suggestion' }
$cause = $answer.cause
if ($cause -notmatch '(?i)no Carrier cost|not (yet )?(arrived|recorded|known)|unknown|no booking') { Write-Fail "the cause does not name the missing Carrier cost: $cause" }
Write-Pass "the real model answered; tools called: $tools"
Write-Host "      Cause: $cause"

# 3. No Spot remark in what the tools returned or were asked.
$toolTraffic = ($toolCalls | ForEach-Object { ($_ | ConvertTo-Json -Depth 20 -Compress) }) -join ' '
if ($toolTraffic -match '(?i)remark') { Write-Fail 'a remark appears in the tool calls' }
if ($SpotRemark -and $toolTraffic -match [regex]::Escape($SpotRemark)) { Write-Fail 'the Planner''s Spot remark appears in the tool calls' }
Write-Pass 'no Spot remark appears in the tool calls'

# 4. The hops: API Management's MCP server and the ERP, with the Agent's identity and the tool name. Ingestion takes minutes: ask again for up to 10.
function Get-HopCount([string] $Flow) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "AppTraces | where TimeGenerated > ago(1h) | where tostring(Properties.runId) == '$correlation' or tostring(Properties.correlationId) == '$correlation' | where tostring(Properties.flow) == '$Flow' | where tostring(Properties.agentId) == '$identityClient' and tostring(Properties.toolName) == 'get_invoice_line' | count"
    $rows.Count -gt 0 ? [int] $rows[0][0] : 0
}
foreach ($flow in 'agent-mcp-finance-matching', 'agent-view-finance-matching') {
    $count = Wait-Until { $n = Get-HopCount $flow; $n -gt 0 ? $n : $null }
    if (-not $count) { Write-Fail "no hop of $flow on the trail $correlation with the Agent's identity and get_invoice_line within 10 minutes" }
    Write-Pass "the Message trail has $count hop(s) of $flow, tagged with the Agent's identity and get_invoice_line"
}

# 5. Nothing kept.
$kept = (Invoke-WebRequest -Uri "$endpoint/openai/v1/responses/$($response.id)" -Headers (Get-FoundryHeaders) -TimeoutSec 60 -SkipHttpErrorCheck).StatusCode
if ($kept -eq 404) {
    Write-Pass 'the service did not keep the response (store: false is honoured when the Agent calls an MCP tool)'
}
else {
    Invoke-WebRequest -Method Delete -Uri "$endpoint/openai/v1/responses/$($response.id)" -Headers (Get-FoundryHeaders) -TimeoutSec 60 -SkipHttpErrorCheck | Out-Null
    Write-Host "NOTE  the service kept the response (HTTP $kept) although store was false; it was deleted. Record this in docs/operations/foundry.md (spike 3)."
}

Write-Host ''
Write-Host 'Open in the ERP UI as Finance, open the line, choose Explain, and see the same answer:'
Write-Host "  $erpUi/carrier-invoices/to-decide"
