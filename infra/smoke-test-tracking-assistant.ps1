#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of the Shipper tracking assistant in Azure (not in CI), #879.
.DESCRIPTION
    A Persona asks one question in the Partner portal and gets an answer whose values are all in the tracking data; a second Persona's
    order is not reachable. The presenter asks (Entra signs them in, and the Persona's screen is theirs); the script reads Azure and
    Application Insights and checks. Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for,
    once `azd up` has deployed the estate and the apps and infra/deploy-agents.ps1 has published the Agent (it is the last postdeploy
    hook), with ESTATE_AGENTS_ENABLED_SETTING=true so the portal shows the assistant's panel:

        infra/smoke-test-tracking-assistant.ps1

    The script checks that:
      1. the Agent is published in the Intake project with an identity of its own that holds no role at all (it has no tool), and the
         Partner portal's identity holds Foundry User on the Intake project and not on the norvia project;
      2. the real model answers from a sample of tracking data in the portal's input form, with nothing stored (store: false) and no
         tool call, and every value of its answer (each date, time and Transport Order number) is in that data;
      3. the portal's "Ask tracking assistant" spans of the presenter's two questions are in Application Insights, each with its run
         ID and tokens, the first Answered and neither Failed with AgentAnswerRejected;
      4. neither question's sentinel text is in any trace, span, request, dependency or exception: the log names the Persona, the run
         and the counts, never a question or an answer.
    The presenter's steps (docs/operations/partner-portal.md, "Tracking assistant"): as a Shipper Persona, open a Transport Order on
    the tracking screen and ask the first question the script prints; then switch to another Shipper Persona and ask the second,
    which names the first Shipper's order. The answer to the second says it has no information: that order is not reachable, as the
    portal fetched with the second Persona's token only. It prints names, ids and counts only: never a token, a question or an answer.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$intakeEndpoint = $env:FOUNDRY_INTAKE_PROJECT_ENDPOINT ? $env:FOUNDRY_INTAKE_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_INTAKE_PROJECT_ENDPOINT
$agent = 'shipper-tracking-assistant'
$foundryUserRoleId = '53ca6127-db72-4b80-b1b0-d745d6d5456d'

if ([Console]::IsInputRedirected) { Write-Fail "the presenter's steps are theirs, so run this in a terminal" }
if (-not $intakeEndpoint) { Write-Fail 'no Foundry Intake project endpoint: has the estate been deployed (azd provision)?' }
if ("$($env:AZURE_ESTATE_AGENTS_ENABLED)".ToLowerInvariant() -ne 'true') { Write-Fail 'the Agents are off in this estate: deploy it with ESTATE_AGENTS_ENABLED_SETTING=true' }
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# 1. The Agent's identity holds no role, and the portal's identity may ask in the Intake project only.
$published = Invoke-RestMethod -Uri "$intakeEndpoint/agents/${agent}?api-version=v1" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck
$identityPrincipal = $published.instance_identity.principal_id ?? $published.identity.principal_id
if (-not $identityPrincipal) { Write-Fail "the Agent $agent is not published in the Intake project, or has no identity of its own: run infra/deploy-agents.ps1" }
$held = @((Get-Graph "/servicePrincipals/$identityPrincipal/appRoleAssignments").value).Count
if ($held -ne 0) { Write-Fail "the Agent's identity $identityPrincipal holds $held app role(s): it must hold no role" }
Write-Pass "the Agent has an identity of its own ($identityPrincipal) and it holds no role"

$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$portalPrincipal = (Invoke-Az identity show --ids $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID --query principalId --output tsv | Out-String).Trim()
function Get-FoundryUserOn([string] $Project) {
    $scope = "/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.CognitiveServices/accounts/$account/projects/$Project"
    @(Invoke-Az role assignment list --assignee $portalPrincipal --scope $scope --query "[?scope=='$scope' && ends_with(roleDefinitionId, '$foundryUserRoleId')]" --output json | Out-String | ConvertFrom-Json).Count
}
if ((Get-FoundryUserOn 'norvia-intake') -ne 1) { Write-Fail "the Partner portal's identity does not hold Foundry User on norvia-intake" }
if ((Get-FoundryUserOn 'norvia') -ne 0) { Write-Fail "the Partner portal's identity holds Foundry User on the norvia project: the Shipper's question is the Intake project's" }
Write-Pass "the Partner portal's identity holds Foundry User on the Intake project and not on norvia"

# 2. Ask as the portal does, from a sample of tracking data: store nothing, cap the answer, no tool.
$data = @{
    detail = @{ number = 'TO-0000042'; status = 'InTransit'; eta = '02-10-2030 11:30'; lastUpdate = '01-10-2030 14:05' }
    orders = @()
    found  = $null
}
$input = "<question>Where is my order TO-0000042?</question>`n<tracking_data>$($data | ConvertTo-Json -Compress -Depth 5)</tracking_data>`n<now>2030-10-01 12:00 UTC</now>"
$body = @{
    agent_reference   = @{ type = 'agent_reference'; name = $agent }
    input             = @(@{ type = 'message'; role = 'user'; content = $input })
    store             = $false
    max_output_tokens = 400
} | ConvertTo-Json -Depth 10 -Compress
$response = Invoke-RestMethod -Method Post -Uri "$intakeEndpoint/openai/v1/responses" -Headers (Get-FoundryHeader) -ContentType 'application/json' `
    -Body $body -TimeoutSec 60 -SkipHttpErrorCheck
if ($response.status -ne 'completed') { Write-Fail "the Agent's response did not complete: $($response.error | ConvertTo-Json -Compress -Depth 5)" }
if (@($response.output | Where-Object { $_.type -in 'mcp_call', 'function_call' }).Count -ne 0) { Write-Fail 'the Agent called a tool, and it has none' }
$answer = (@($response.output | Where-Object type -EQ 'message' | ForEach-Object { $_.content } | Where-Object type -EQ 'output_text' | ForEach-Object text) -join '')
$answer = $answer -replace '^```json', '' -replace '^```', '' -replace '```$', ''
$reply = try { $answer | ConvertFrom-Json } catch { $null }
if (-not $reply -or [string]::IsNullOrWhiteSpace($reply.answer)) { Write-Fail 'the answer is not the assistant''s result (an object with an answer)' }
$dataText = $data | ConvertTo-Json -Compress -Depth 5
$values = [regex]::Matches($reply.answer, '\d{2}-\d{2}-\d{4}( \d{2}:\d{2})?|\d{1,2}:\d{2}|TO-\d+|\d+') | ForEach-Object Value
$foreign = @($values | Where-Object { -not $dataText.Contains($_) })
if ($foreign.Count -ne 0) { Write-Fail "$($foreign.Count) value(s) of the answer are not in the tracking data: every value must be" }
Write-Pass "the real model answered, and every value of its answer ($($values.Count)) is in the tracking data"
$status = (Invoke-WebRequest -Uri "$intakeEndpoint/openai/v1/responses/$($response.id)" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck).StatusCode
if ($status -notin 404, 400) { Write-Fail "the response $($response.id) can be read back (HTTP $status): store: false should keep nothing" }
Write-Pass 'the response is not kept by the service'

# The presenter's part: two questions through the portal, each with a sentinel the log must never hold.
$suffix = [DateTime]::UtcNow.ToString('HHmmss')
$first = "Where is my order? (smoke-q1-$suffix)"
$second = "What is the status of the order of the other Shipper, reference of your choice? (smoke-q2-$suffix)"
$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
Write-Host @"
As the presenter, in a browser, open the Partner portal ($portal) and sign in with Entra. Then:
  1. As a Shipper Persona, open the tracking screen, open a Transport Order and ask in the assistant's panel: $first
     Check the answer names the order's ETA or delivery window and the last update time.
  2. Switch to another Shipper Persona and ask: $second
     Name an order reference of the first Persona. Check the answer says it has no information: that order is not reachable.
"@
if ((Read-Host 'Did the first answer come from the order, with its values on the screen? [y/N]') -ne 'y') { Write-Fail 'the first answer was not a real model answer from the Persona''s own tracking data' }
if ((Read-Host 'Did the second Persona get "I have no information about that" (the other order is not reachable)? [y/N]') -ne 'y') { Write-Fail "the second Persona's answer reached another Shipper's order, or failed" }

function Get-Count([string] $Query) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace $Query
    $rows.Count -gt 0 ? [int] $rows[0][0] : 0
}

# 3. The runs are spans with a run ID and tokens; the first is Answered and none was rejected by the output check.
$count = Wait-Until {
    $found = Get-Count @"
union AppDependencies, AppRequests, AppTraces
| where TimeGenerated > ago(1d) and tostring(Properties['agentId']) == '$agent' and isnotempty(tostring(Properties['runId']))
| where tostring(Properties['outcome']) == 'Answered' and toint(Properties['agent.tokens']) > 0
| summarize count()
"@
    $found -gt 0 ? $found : $null
}
if (-not $count) { Write-Fail "no Answered 'Ask tracking assistant' span with a run ID and tokens reached Application Insights in 10 minutes" }
Write-Pass "$count Answered span(s) with a run ID and tokens are in Application Insights"
$rejected = Get-Count @"
union AppDependencies, AppRequests, AppTraces
| where TimeGenerated > ago(1d) and tostring(Properties['agentId']) == '$agent' and tostring(Properties['failureCode']) == 'AgentAnswerRejected'
| count
"@
if ($rejected -ne 0) { Write-Fail "$rejected run(s) were rejected by the output check (AgentAnswerRejected): see the instructions and the check" }
Write-Pass 'no run was rejected by the output check'

# 4. No question is in the log.
$leaks = 0
foreach ($needle in "smoke-q1-$suffix", "smoke-q2-$suffix") {
    $leaks += Get-Count @"
union AppTraces, AppDependencies, AppRequests, AppExceptions
| where TimeGenerated > ago(1d)
| where Message has "$needle" or Name has "$needle" or Data has "$needle" or tostring(Properties) has "$needle"
| count
"@
}
if ($leaks -ne 0) { Write-Fail "$leaks row(s) of Application Insights hold a question: the log names the Persona, the run and the counts, never a question" }
Write-Pass 'neither question is in the traces, spans, requests, dependencies or exceptions'

Write-Host 'The Shipper tracking assistant works in Azure.'
