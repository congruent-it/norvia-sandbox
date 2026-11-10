#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the Integration operations agent in Azure (not in CI).
.DESCRIPTION
    Post-deploy smoke run of the Integration operations agent in Azure (not in CI). Run it in a terminal, signed in to the Azure CLI as the
    presenter the estate was provisioned for (the presenter holds the Foundry User role on the project), once `azd up` has deployed the estate
    and the apps and infra/deploy-agents.ps1 has published the Agent (it is the last postdeploy hook), with ESTATE_AGENTS_ENABLED_SETTING=true so
    the console shows Explain:

      infra/smoke-test-integration-operations-agent.ps1 [<correlation ID of a trail with the unmappable code>]

    The trail is P5's unmappable code: the Carrier-tracking Logic App's run fails on a code outside its mapping table, such as DLV, and the
    message lands on the tracking queue's dead-letter queue (infra/smoke-test-shipper-visibility.ps1 sends one, and so does the Partner portal's
    presenter panel fault). Without an ID the script takes the latest such trail from Application Insights.

    It asks the Agent what the console's Explain button asks it (through the project, as the presenter, which the console does as its own
    identity) and checks that:
      1. the Agent has its own identity and holds only IntegrationConsole.Agent.Read, on the agent view and nothing else; the console's agent API
         has that one role, held by API Management's identity alone; the project's shared identity holds no role;
      2. the real model answers with a Suggestion that names the cause, cites a result, and has called only get_message_trail and get_run;
      3. the Agent's tool calls are hops of the Message trail, in API Management's MCP server and in the console, tagged with the Agent's
         identity and the tool name, under the same correlation ID as the trail;
      4. the response is not kept by the service (store: false), or is gone once it is deleted.
    The Explain button itself is pressed by hand: the script prints the console address of the trail. It prints names, ids and counts only: never
    a token, a prompt or an answer's text beyond the cause.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Trail = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT
$contract = Get-Content -Raw (Join-Path $repo 'contracts/agent-mcp/integration-operations.tools.json') | ConvertFrom-Json
$agent = 'integration-operations'
$role = $contract.role

if (-not $endpoint) { Write-Fail 'no Foundry project endpoint: has the estate been deployed (azd provision)?' }
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

if (-not $Trail) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "union AppTraces, AppRequests, AppDependencies | where TimeGenerated > ago(1d) | where tostring(Properties.failureCode) == 'UnmappableTrackingMessage' | top 1 by TimeGenerated desc | project tostring(Properties.correlationId)"
    $Trail = $rows.Count -gt 0 ? $rows[0][0] : ''
    if (-not $Trail) { Write-Fail "no trail with the unmappable code in the last day: send a Carrier's DLV first (infra/smoke-test-shipper-visibility.ps1, or the Partner portal's presenter panel)" }
}
Write-Host "Trail: $Trail"

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

$api = Get-Graph "/servicePrincipals?`$filter=displayName eq 'Norvia Integration console agent API ($($env:AZURE_ENV_NAME))'&`$select=id,appRoles"
$apiId = @($api.value)[0].id
if (-not $apiId) { Write-Fail "the console's agent API is not in Entra: has the estate been deployed?" }
$enabledRoles = @(@($api.value)[0].appRoles | Where-Object isEnabled)
if (-not ($enabledRoles.Count -eq 1 -and $enabledRoles[0].value -eq $role)) { Write-Fail "$role is not the only role on the console's agent API" }
$holders = (@((Get-Graph "/servicePrincipals/$apiId/appRoleAssignedTo").value | ForEach-Object principalDisplayName)) -join ','
if ($holders -notlike 'id-apim-*') { Write-Fail "the console's agent API is held by '$holders', not by API Management's identity alone" }
Write-Pass "$role is the only role on the console's agent API, and API Management's identity ($holders) alone holds it"

$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$projectIdentity = (Invoke-Az resource show --resource-group $resourceGroup --resource-type Microsoft.CognitiveServices/accounts/projects `
        --name "$account/norvia" --query identity.principalId --output tsv | Out-String).Trim()
if ($projectIdentity) {
    if (@((Get-Graph "/servicePrincipals/$projectIdentity/appRoleAssignments").value).Count -ne 0) { Write-Fail "the project's shared identity holds a role" }
    Write-Pass "the project's shared identity holds no role"
}

# 2. Ask, as Explain does.
$body = @{
    agent_reference   = @{ type = 'agent_reference'; name = $agent }
    input             = @(@{ type = 'message'; role = 'user'; content = "Explain why the Message trail $Trail failed." })
    store             = $false
    max_tool_calls    = 6
    max_output_tokens = 1200
    structured_inputs = @{ correlationId = $Trail }
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
if ($calls.name -notcontains 'get_message_trail') { Write-Fail 'the Agent did not read the Message trail' }
$answer = (@($response.output | Where-Object type -EQ 'message' | ForEach-Object { $_.content } | Where-Object type -EQ 'output_text' | ForEach-Object text) -join '') | ConvertFrom-Json
if (-not ($answer.cause -and @($answer.evidence).Count -gt 0 -and $answer.suggestedAction -and $answer.confidence -in 'low', 'medium', 'high')) { Write-Fail 'the answer is not a Suggestion' }
$cause = $answer.cause
if ($cause -notmatch 'DLV|map|unmapp|status code|tracking code') { Write-Fail "the cause does not name the unmappable code: $cause" }
Write-Pass "the real model answered; tools called: $tools"
Write-Host "      Cause: $cause"

# 3. The hops: API Management's MCP server and the console, with the Agent's identity and the tool name. Ingestion takes minutes: ask again for up to 10.
function Get-HopCount([string] $Flow) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "AppTraces | where TimeGenerated > ago(1h) | where tostring(Properties.runId) == '$Trail' | where tostring(Properties.flow) == '$Flow' | where tostring(Properties.agentId) == '$identityClient' and tostring(Properties.toolName) == 'get_message_trail' | count"
    $rows.Count -gt 0 ? [int] $rows[0][0] : 0
}
foreach ($flow in 'agent-mcp-integration-operations', 'agent-view-integration-operations') {
    $count = Wait-Until { $n = Get-HopCount $flow; $n -gt 0 ? $n : $null }
    if (-not $count) { Write-Fail "no hop of $flow on the trail $Trail with the Agent's identity and get_message_trail within 10 minutes" }
    Write-Pass "the Message trail has $count hop(s) of $flow, tagged with the Agent's identity and get_message_trail"
}

# 4. Nothing kept.
$kept = (Invoke-WebRequest -Uri "$endpoint/openai/v1/responses/$($response.id)" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck).StatusCode
if ($kept -eq 404) {
    Write-Pass 'the service did not keep the response (store: false is honoured when the Agent calls an MCP tool)'
}
else {
    Invoke-WebRequest -Method Delete -Uri "$endpoint/openai/v1/responses/$($response.id)" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck | Out-Null
    Write-Host "NOTE  the service kept the response (HTTP $kept) although store was false; it was deleted. Record this in docs/operations/foundry.md (spike 3)."
}

$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
Write-Host ''
Write-Host 'Open in the Integration console, choose Explain, and see the same answer and the Agent''s hops on the trail:'
Write-Host "  $console/?key=$Trail"
