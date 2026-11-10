#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the Planner agent in Azure (not in CI).
.DESCRIPTION
    Post-deploy smoke run of the Planner agent in Azure (not in CI). Run it in a terminal, signed in to the Azure CLI as the presenter
    the estate was provisioned for (the presenter holds the Foundry User role on the project), once `azd up` has deployed the estate and
    the apps and infra/deploy-agents.ps1 has published the Agent (it is the last postdeploy hook), with ESTATE_AGENTS_ENABLED_SETTING=true
    so the TMS UI shows Suggest.

    The Transport Order must be waiting for planning, collect on the Over capacity day, and be an Own fleet candidate, so that Own fleet
    fits only with a warning. infra/smoke-test-over-capacity-day.ps1 prints the day; book one more Transport Order from Rotterdam as
    Brouwer Dranken in the Partner portal for that day, leave it unassigned, and pass the number the portal showed:

      infra/smoke-test-planner-agent.ps1 <Transport Order number>

    It asks the Agent what the TMS UI's Suggest button asks it (through the project, as the presenter, which the TMS UI does as its own
    identity) and checks that:
      1. the Agent has its own identity and holds only TransportOrders.PlannerAgent.Read, on the Planner agent view and nothing else; the
         role is on the TMS agent API, held by API Management's identity alone; the project's shared identity holds no role;
      2. the real model answers with a Suggestion that names the Over capacity warning, compares a Carrier's cost, cites a result and has
         called only get_assignment_options, once;
      3. no street address and no Consignee field appears in the tool calls;
      4. the Agent's tool call is a hop of the run's Message trail, in API Management's MCP server and in the TMS, tagged with the Agent's
         identity and the tool name;
      5. the response is not kept by the service (store: false), or is gone once it is deleted.
    The Suggest button itself is pressed by hand: the script prints the TMS UI address. It prints names, ids and counts only: never a
    token, a prompt, or an answer's text beyond the cause.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Number = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT
$contract = Get-Content -Raw (Join-Path $repo 'contracts/agent-mcp/planner.tools.json') | ConvertFrom-Json
$agent = 'planner'
$role = $contract.role
$tool = @($contract.tools)[0].name
$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup

if (-not $endpoint) { Write-Fail 'no Foundry project endpoint: has the estate been deployed (azd provision)?' }
if ($Number -notmatch '^TO-[0-9]+$') { Write-Fail 'usage: smoke-test-planner-agent.ps1 <Transport Order number>, such as TO-0000042 (waiting for planning, collecting on the Over capacity day: see smoke-test-over-capacity-day.ps1)' }
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup
function Get-FoundryHeader { @{ Authorization = "Bearer $(Get-AzAccessToken https://ai.azure.com)" } }
function Get-Graph([string] $Path) { Invoke-AzRest -Method get -Url "https://graph.microsoft.com/v1.0$Path" }

# 1. The identities and their roles.
$published = Invoke-RestMethod -Uri "$endpoint/agents/${agent}?api-version=v1" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck -StatusCodeVariable publishedStatus
if ($publishedStatus -ge 400) { Write-Fail "Foundry answered HTTP ${publishedStatus} when asked for the Agent ${agent}: $($published.error.code ?? 'no-detail')" }
$identityPrincipal = $published.instance_identity.principal_id ?? $published.identity.principal_id
$identityClient = $published.instance_identity.client_id ?? $published.identity.client_id
if (-not ($identityPrincipal -and $identityClient)) { Write-Fail "the Agent $agent is not published, or has no identity of its own: run infra/deploy-agents.ps1" }
$view = Get-Graph "/servicePrincipals?`$filter=displayName eq 'Norvia $agent agent view ($($env:AZURE_ENV_NAME))'&`$select=id,appRoles"
$viewId = @($view.value)[0].id
if (-not $viewId) { Write-Fail 'the Planner agent view is not in Entra: has the estate been deployed?' }
$held = @((Get-Graph "/servicePrincipals/$identityPrincipal/appRoleAssignments").value | ForEach-Object { @{ resource = $_.resourceId; role = $_.appRoleId } })
$expectedRoleId = (@($view.value)[0].appRoles | Where-Object value -EQ $role).id
if (-not ($held.Count -eq 1 -and $held[0].resource -eq $viewId -and $held[0].role -eq $expectedRoleId)) {
    Write-Fail "the Agent's identity does not hold exactly $role on the Planner agent view: $($held | ConvertTo-Json -Compress)"
}
Write-Pass "the Agent's identity $identityClient holds $role on the Planner agent view and nothing else"

# The TMS agent API has a role for each of the TMS's Agents; the Planner's is held by API Management's identity alone.
$api = Get-Graph "/servicePrincipals?`$filter=displayName eq 'Norvia TMS agent API ($($env:AZURE_ENV_NAME))'&`$select=id,appRoles"
$apiId = @($api.value)[0].id
if (-not $apiId) { Write-Fail 'the TMS agent API is not in Entra: has the estate been deployed?' }
$apiRole = @(@($api.value)[0].appRoles | Where-Object { $_.isEnabled -and $_.value -eq $role })
if ($apiRole.Count -ne 1) { Write-Fail "$role is not a role of the TMS agent API" }
$holders = @((Get-Graph "/servicePrincipals/$apiId/appRoleAssignedTo").value | Where-Object appRoleId -EQ $apiRole[0].id | ForEach-Object principalDisplayName)
if ($holders.Count -ne 1 -or $holders[0] -notlike 'id-apim-*') { Write-Fail "$role on the TMS agent API is held by '$($holders -join ',')', not by API Management's identity alone" }
Write-Pass "$role on the TMS agent API is held by API Management's identity ($($holders[0])) alone"

$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$projectIdentity = (Invoke-Az resource show --resource-group $resourceGroup --resource-type Microsoft.CognitiveServices/accounts/projects `
        --name "$account/norvia" --query identity.principalId --output tsv | Out-String).Trim()
if ($projectIdentity) {
    if (@((Get-Graph "/servicePrincipals/$projectIdentity/appRoleAssignments").value).Count -ne 0) { Write-Fail "the project's shared identity holds a role" }
    Write-Pass "the project's shared identity holds no role"
}

# 2. Ask, as Suggest does (the question is the TMS UI's fixed one, PlannerQuestions.SuggestAssignment). The run has a correlation ID of its own:
# the script finds its tool calls by it.
$correlation = [guid]::NewGuid().ToString()
$body = @{
    agent_reference   = @{ type = 'agent_reference'; name = $agent }
    input             = @(@{ type = 'message'; role = 'user'; content = "Suggest who should carry the Transport Order $Number, with the reasoning and the trade-off against the other options." })
    store             = $false
    max_tool_calls    = 1
    max_output_tokens = 1200
    structured_inputs = @{ correlationId = $correlation }
} | ConvertTo-Json -Depth 10 -Compress
$response = Invoke-RestMethod -Method Post -Uri "$endpoint/openai/v1/responses" -Headers (Get-FoundryHeader) -ContentType 'application/json' -Body $body -TimeoutSec 180 -SkipHttpErrorCheck
if ($response.status -ne 'completed') { Write-Fail "the Agent's response did not complete: $(($response.error ?? @{ status = $response.status }) | ConvertTo-Json -Compress -Depth 5)" }
$calls = @($response.output | Where-Object type -EQ 'mcp_call')
if ($calls.Count -eq 0) { Write-Fail 'the Agent called no tool' }
if ($calls.Count -ne 1 -or $calls[0].name -ne $tool) { Write-Fail "the Agent did not call $tool exactly once: $((@($calls.name)) -join ',')" }
if (@($calls | Where-Object { $_.name -notin @($contract.tools.name) }).Count -gt 0) { Write-Fail 'the Agent called a tool that is not on the contract' }
$refused = @($calls | Where-Object { $null -ne $_.error -and $_.error -ne '' -and "$($_.error | ConvertTo-Json -Compress)" -ne '{}' })
if ($refused.Count -gt 0) { Write-Fail "the tool call was refused: $($refused | ForEach-Object { @{ name = $_.name; error = $_.error } } | ConvertTo-Json -Compress -Depth 5)" }
$answer = (@($response.output | Where-Object type -EQ 'message' | ForEach-Object { $_.content } | Where-Object type -EQ 'output_text' | ForEach-Object text) -join '') | ConvertFrom-Json
if (-not ($answer.cause -and @($answer.evidence).Count -gt 0 -and $answer.suggestedAction -and $answer.confidence -in 'low', 'medium', 'high')) { Write-Fail 'the answer is not a Suggestion' }
if ($answer.action -ne 'needs-a-person') { Write-Fail "the Suggestion is not left to a person: $($answer.action)" }

# The options the tool returned: Own fleet must be a candidate with a warning, and some Carrier priced, or the answer has nothing to name.
$options = $calls[0].output | ConvertFrom-Json
if ($options.ownFleet.fit -ne 'OverCapacity') { Write-Fail "$Number is not an Own fleet candidate that is Over capacity (Own fleet: $($options.ownFleet.fit)): book it for the Over capacity day, unassigned" }
$priced = @($options.carriers | Where-Object { $null -ne $_.carrierCost })
if ($priced.Count -eq 0) { Write-Fail "no Carrier can be priced for $Number, so there is no cost to compare" }
$text = "$($answer.cause) $($answer.suggestedAction) $(@($answer.evidence).claim -join ' ')"
if ($text -notmatch '(?i)over\s*capacity|over the day''s capacity|more than the .*capacity') { Write-Fail "the answer does not name the Over capacity warning: $($answer.cause)" }
# '412.50' contains '412.5', and '412' is in both: the answer may write the figure either way.
$compared = @($priced | Where-Object { $text -match [regex]::Escape($_.name) -and $text.Contains(([decimal] $_.carrierCost).ToString('0.##', [cultureinfo]::InvariantCulture)) })
if ($compared.Count -eq 0) { Write-Fail "the answer does not compare a Carrier's cost (no priced Carrier is named with its cost): $($answer.cause)" }
Write-Pass "the real model answered, named the Over capacity warning and compared a Carrier's cost; tool called: $tool"
Write-Host "      Cause: $($answer.cause)"

# 3. No street address and no Consignee field in what the tool returned or was asked.
$toolTraffic = ($calls | ForEach-Object { ($_ | ConvertTo-Json -Depth 20 -Compress) }) -join ' '
foreach ($forbidden in @('street', 'address', 'consignee', 'receiver', 'contact', 'houseNumber')) {
    if ($toolTraffic -match "(?i)$forbidden") { Write-Fail 'a street address or Consignee field appears in the tool calls' }
}
if ($toolTraffic -cmatch '\b[0-9]{4} ?[A-Z]{2}\b') { Write-Fail 'a full postcode appears in the tool calls (only the area is allowed)' }
Write-Pass 'no street address or Consignee field appears in the tool calls'

# 4. The hops: API Management's MCP server and the TMS, with the Agent's identity and the tool name. Ingestion takes minutes: ask again for up to 10.
function Get-HopCount([string] $Flow) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "AppTraces | where TimeGenerated > ago(1h) | where tostring(Properties.runId) == '$correlation' or tostring(Properties.correlationId) == '$correlation' | where tostring(Properties.flow) == '$Flow' | where tostring(Properties.agentId) == '$identityClient' and tostring(Properties.toolName) == '$tool' | count"
    $rows.Count -gt 0 ? [int] $rows[0][0] : 0
}
foreach ($flow in 'agent-mcp-planner', 'agent-view-planner') {
    $count = Wait-Until { $n = Get-HopCount $flow; $n -gt 0 ? $n : $null }
    if (-not $count) { Write-Fail "no hop of $flow on the trail $correlation with the Agent's identity and $tool within 10 minutes" }
    Write-Pass "the Message trail has $count hop(s) of $flow, tagged with the Agent's identity and $tool"
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
Write-Host 'Open in the TMS UI as the Planner, choose Suggest on the Transport Order, and see the same kind of answer:'
Write-Host "  $tmsUi/transport-orders"
