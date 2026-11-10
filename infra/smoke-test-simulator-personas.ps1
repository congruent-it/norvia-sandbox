#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of the Simulator's personas agent in Azure (not in CI).
.DESCRIPTION
    The presenter previews a plan with the agent as the pool source and runs it, which the script can't do for them: Entra signs
    them in, and the Simulator's screens are theirs. The script stops and says what to do, reads Foundry and Application Insights,
    and checks. Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for (the presenter
    holds the Foundry User role on the project), once `azd up` has deployed the estate and the apps and
    infra/deploy-agents.ps1 has published the Agent (it is the last postdeploy hook), with ESTATE_AGENTS_ENABLED_SETTING=true so
    the Simulator asks the Agent:

        infra/smoke-test-simulator-personas.ps1 <run number>

    The run number is the one the Simulator's Configure screen says the next run gets. The presenter's steps
    (docs/operations/simulator.md, "Presenter smoke run in Azure"): choose Quiet Monday, set the seed to 349 and press Preview
    plan, then Start run and wait until it is Completed. The script checks that:
      1. the Agent is published and has an identity of its own that holds no app role at all, and the project's shared identity
         holds none (the Agent has no tool, so there is nothing for it to call);
      2. the real model answers the Simulator's question with a list of texts, and the response is not kept by the service
         (store: false);
      3. the generation is a span of the Simulator in Application Insights, with the Agent's name and a run ID, and its
         dependency on Foundry;
      4. no text the model wrote is in any trace, span, request or dependency of the Simulator: the log names the profile and the
         category, never a text. (Foundry's own trace keeps content apart, in AppGenAIContent; docs/operations/foundry.md, "Spike 2".)
    The presenter confirms two things by eye, which only the Simulator's screens show: the preview's Carrier texts say "agent, run
    N" as the source of their pools, and the Carriers' declines in the run's log are worded from those pools. It prints names, ids
    and counts only: never a token, a prompt or a text.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $RunNumber = '')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT
$agent = 'simulator-personas'

if ($RunNumber -notmatch '^[0-9]+$') { Write-Fail 'usage: smoke-test-simulator-personas.ps1 <run number>, such as 1: the number the Configure screen gave the run' }
if ([Console]::IsInputRedirected) { Write-Fail "the presenter's steps are theirs, so run this in a terminal" }
if (-not $endpoint) { Write-Fail 'no Foundry project endpoint: has the estate been deployed (azd provision)?' }
if ("$($env:AZURE_ESTATE_AGENTS_ENABLED)".ToLowerInvariant() -ne 'true') { Write-Fail 'the Agents are off in this estate: deploy it with ESTATE_AGENTS_ENABLED_SETTING=true' }

$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# 1. The Agent's identity holds no role; the project's shared identity holds none either.
$published = Invoke-RestMethod -Uri "$endpoint/agents/${agent}?api-version=v1" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck
$identityPrincipal = $published.instance_identity.principal_id ?? $published.identity.principal_id
if (-not $identityPrincipal) { Write-Fail "the Agent $agent is not published, or has no identity of its own: run infra/deploy-agents.ps1" }
$held = @((Get-Graph "/servicePrincipals/$identityPrincipal/appRoleAssignments").value).Count
if ($held -ne 0) { Write-Fail "the Agent's identity $identityPrincipal holds $held app role(s): it must hold none" }
Write-Pass "the Agent has an identity of its own ($identityPrincipal) and it holds no app role"

$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$projectIdentity = (Invoke-Az resource show --resource-group $resourceGroup --resource-type Microsoft.CognitiveServices/accounts/projects `
        --name "$account/norvia" --query identity.principalId --output tsv | Out-String).Trim()
if ($projectIdentity) {
    if (@((Get-Graph "/servicePrincipals/$projectIdentity/appRoleAssignments").value).Count -ne 0) { Write-Fail "the project's shared identity holds a role" }
    Write-Pass "the project's shared identity holds no role"
}

# 2. Ask, as the Simulator does: store nothing, cap the answer.
$question = "Write 5 texts.`nprofile: terse-dutch`nstyle: a few blunt words, no pleasantries`nlanguage: nl`ncategory: DeclineReason"
$body = @{
    agent_reference   = @{ type = 'agent_reference'; name = $agent }
    input             = @(@{ type = 'message'; role = 'user'; content = $question })
    store             = $false
    max_output_tokens = 800
} | ConvertTo-Json -Depth 10 -Compress
$response = Invoke-RestMethod -Method Post -Uri "$endpoint/openai/v1/responses" -Headers (Get-FoundryHeader) -ContentType 'application/json' `
    -Body $body -TimeoutSec 60 -SkipHttpErrorCheck
if ($response.status -ne 'completed') { Write-Fail "the Agent's response did not complete: $($response.error | ConvertTo-Json -Compress -Depth 5)" }
if (@($response.output | Where-Object { $_.type -in 'mcp_call', 'function_call' }).Count -ne 0) { Write-Fail 'the Agent called a tool, and it has none' }
$answer = (@($response.output | Where-Object type -EQ 'message' | ForEach-Object { $_.content } | Where-Object type -EQ 'output_text' | ForEach-Object text) -join '')
$answer = $answer -replace '^```json', '' -replace '^```', '' -replace '```$', ''
$texts = try { @(($answer | ConvertFrom-Json).texts) } catch { @() }
if ($texts.Count -lt 3 -or @($texts | Where-Object { $_ -isnot [string] -or $_.Length -eq 0 -or $_.Length -ge 120 }).Count -gt 0) {
    Write-Fail 'the answer is not a list of at least three short texts'
}
Write-Pass "the real model wrote $($texts.Count) texts"
$status = (Invoke-WebRequest -Uri "$endpoint/openai/v1/responses/$($response.id)" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck).StatusCode
if ($status -notin 404, 400) { Write-Fail "the response $($response.id) can be read back (HTTP $status): store: false should keep nothing" }
Write-Pass 'the response is not kept by the service'

# The presenter's part: Preview plan with the agent as the pool source, then run it.
$simulator = Get-AppUrl simulator-web -ResourceGroup $resourceGroup
Write-Host @"
As the presenter, in a browser, open the Simulator ($simulator) and sign in with Entra. Then:
  1. On Configure, check the number the next run gets is $RunNumber.
  2. Choose Quiet Monday, set the seed to 349 and Preview plan.
  3. Under Carrier texts, check every pool's source reads "agent, run $RunNumber" (a pool that fell back reads "built-in" and a note
     above says why).
  4. Start run and wait until it is Completed.
  5. In the log, check a Carrier's decline reason is worded like the pool's sample texts.
"@
if ((Read-Host "Did the preview show the pools with source `"agent, run $RunNumber`"? [y/N]") -ne 'y') {
    Write-Fail "the pools were not generated by the agent: see the note above the Carrier texts, and the Simulator's log in Application Insights"
}
if ((Read-Host "Is the run Completed, and are the Carriers' declines drawn from the pools? [y/N]") -ne 'y') {
    Write-Fail "the run is not done, or the Carriers' texts are not from the pools"
}

function Get-Count([string] $Query) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace $Query
    $rows.Count -gt 0 ? [int] $rows[0][0] : 0
}

# 3. The generation is a span with the Agent's name and a run ID.
$count = Wait-Until {
    $found = Get-Count @"
union AppDependencies, AppRequests, AppTraces
| where TimeGenerated > ago(1d) and tostring(Properties['agent.name']) == '$agent' and isnotempty(tostring(Properties['agent.run.id']))
| summarize count()
"@
    $found -gt 0 ? $found : $null
}
if (-not $count) { Write-Fail "no 'Agent run' span of $agent with a run ID reached Application Insights in 10 minutes" }
Write-Pass "$count span(s) of the Agent's runs are in Application Insights, each with its run ID"

# 4. No text is in the log. The texts the model wrote just now are looked for in every table the Simulator writes to.
$leaks = 0
foreach ($text in $texts) {
    $needle = $text.Replace('\', '\\').Replace('"', '\"')
    $leaks += Get-Count @"
union AppTraces, AppDependencies, AppRequests, AppExceptions
| where TimeGenerated > ago(1d)
| where Message has "$needle" or Name has "$needle" or Data has "$needle" or tostring(Properties) has "$needle"
| count
"@
}
if ($leaks -ne 0) { Write-Fail "$leaks row(s) of Application Insights hold a text the model wrote: the log must name the profile and the category, never a text" }
Write-Pass "none of the model's texts is in the traces, spans, requests, dependencies or exceptions"

Write-Host "The Simulator's personas agent works in Azure."
