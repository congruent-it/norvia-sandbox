#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the Estate health check in Azure (not in CI).
.DESCRIPTION
    Post-deploy smoke run of the Estate health check in Azure (not in CI). Run it in a terminal, signed in to the Azure CLI as the presenter the
    estate was provisioned for (the presenter holds the Foundry User role on the project), once `azd up` has deployed the estate and the apps and
    infra/deploy-agents.ps1 has published the Agent (it is the last postdeploy hook), with ESTATE_AGENTS_ENABLED_SETTING=true so the page narrates:

      infra/smoke-test-estate-health-check.ps1

    It asks the Agent what the page's "Run health check" asks it (through the project, as the presenter; the page does it as its own identity), with
    the as-found alert toggle on and again with it off, and checks that:
      1. the identities: the Agent has its own identity and holds only HealthCheck.Agent.Read, on the agent view and nothing else, and no Azure
         role; the app's agent API has that one role, held by API Management's identity alone; the app's identity holds Reader on the estate's
         resource group and no other role on it; the project's shared identity holds no role (#371, question 1);
      2. with the as-found preset (entitiesWithoutDeadLetterAlert lists the queues and subscriptions without a dead-letter alert, and the other
         toggles of the preset are on too) the checks return the Finding F-DLQ-ALERT among others, naming carrier-tracking, and the real model
         explains it, citing the tool's result;
      3. with the fixed preset, after the estate is deployed again, the alerts are back and the checks return no Finding: every Finding is gone.
         The fixed estate has an alert receiver only when ESTATE_ALERT_EMAIL is set (azd env set), so set it first: without one the check
         ALERT-RECEIVER rightly finds the alerts go to nobody;
      4. the Agent's tool calls are hops of the run's Message trail, in API Management's MCP server and in the app, tagged with the Agent's identity
         and the tool name, under the correlation ID of the run.
    The estate ends in the fixed state. Resource Graph takes a short while to see an alert come and go, so a check asks again, up to ten minutes. It
    prints names, ids and counts only: never a token, a prompt or an answer's text beyond a Finding's ID.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT
$contract = Get-Content -Raw (Join-Path $repo 'contracts/agent-mcp/health-check.tools.json') | ConvertFrom-Json
$agent = 'estate-health-check'
$role = $contract.role
$correlation = "smoke-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"

if (-not $endpoint) { Write-Fail 'no Foundry project endpoint: has the estate been deployed (azd provision)?' }
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

# 1. The identities and their roles.
$published = Invoke-RestMethod -Uri "$endpoint/agents/${agent}?api-version=v1" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck
$identityPrincipal = $published.instance_identity.principal_id ?? $published.identity.principal_id
$identityClient = $published.instance_identity.client_id ?? $published.identity.client_id
if (-not ($identityPrincipal -and $identityClient)) { Write-Fail "the Agent $agent is not published, or has no identity of its own: run infra/deploy-agents.ps1" }
$view = Get-Graph "/servicePrincipals?`$filter=displayName eq 'Norvia health-check agent view ($($env:AZURE_ENV_NAME))'&`$select=id,appRoles"
$viewId = @($view.value)[0].id
if (-not $viewId) { Write-Fail 'the agent view is not in Entra: has the estate been deployed?' }
$held = @((Get-Graph "/servicePrincipals/$identityPrincipal/appRoleAssignments").value | ForEach-Object { @{ resource = $_.resourceId; role = $_.appRoleId } })
$expectedRoleId = (@($view.value)[0].appRoles | Where-Object value -EQ $role).id
if (-not ($held.Count -eq 1 -and $held[0].resource -eq $viewId -and $held[0].role -eq $expectedRoleId)) {
    Write-Fail "the Agent's identity does not hold exactly $role on the agent view: $($held | ConvertTo-Json -Compress)"
}
Write-Pass "the Agent's identity $identityClient holds $role on the agent view and nothing else"

$azureRoles = [int] (Invoke-Az role assignment list --all --assignee $identityPrincipal --query 'length(@)' --output tsv | Out-String).Trim()
if ($azureRoles -ne 0) { Write-Fail "the Agent's identity holds $azureRoles Azure role(s): it must hold none, Reader belongs to the app's identity" }
Write-Pass "the Agent's identity holds no Azure role"

$api = Get-Graph "/servicePrincipals?`$filter=displayName eq 'Norvia Estate health check agent API ($($env:AZURE_ENV_NAME))'&`$select=id,appRoles"
$apiId = @($api.value)[0].id
if (-not $apiId) { Write-Fail "the app's agent API is not in Entra: has the estate been deployed?" }
$enabledRoles = @(@($api.value)[0].appRoles | Where-Object isEnabled)
if (-not ($enabledRoles.Count -eq 1 -and $enabledRoles[0].value -eq $role)) { Write-Fail "$role is not the only role on the app's agent API" }
$holders = (@((Get-Graph "/servicePrincipals/$apiId/appRoleAssignedTo").value | ForEach-Object principalDisplayName)) -join ','
if ($holders -notlike 'id-apim-*') { Write-Fail "the app's agent API is held by '$holders', not by API Management's identity alone" }
Write-Pass "$role is the only role on the app's agent API, and API Management's identity ($holders) alone holds it"

$appIdentity = (Invoke-Az identity show --resource-group $resourceGroup --name $env:AZURE_ESTATE_HEALTH_CHECK_IDENTITY_NAME --query principalId --output tsv | Out-String).Trim()
$appRoles = Invoke-Az role assignment list --all --assignee $appIdentity --query '[].{role: roleDefinitionName, scope: scope}' --output json | Out-String | ConvertFrom-Json
$rgId = (Invoke-Az group show --name $resourceGroup --query id --output tsv | Out-String).Trim()
$onResourceGroup = @($appRoles | Where-Object { $_.scope.StartsWith($rgId, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object role | Sort-Object -Unique)
if (-not (Test-SameValues $onResourceGroup 'Reader')) {
    Write-Fail "the app's identity holds other roles on the estate's resource group than Reader: $(@($appRoles.role) | ConvertTo-Json -Compress)"
}
Write-Pass "the app's identity holds Reader on the estate's resource group and no other role on it"

$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$projectIdentity = (Invoke-Az resource show --resource-group $resourceGroup --resource-type Microsoft.CognitiveServices/accounts/projects `
        --name "$account/norvia" --query identity.principalId --output tsv | Out-String).Trim()
if ($projectIdentity) {
    if (@((Get-Graph "/servicePrincipals/$projectIdentity/appRoleAssignments").value).Count -ne 0) { Write-Fail "the project's shared identity holds a role" }
    Write-Pass "the project's shared identity holds no role"
}

# Asks the Agent what the page's button asks it, once. Returns the response; $correlation is the run's correlation ID.
function Invoke-Ask {
    $body = @{
        agent_reference   = @{ type = 'agent_reference'; name = $agent }
        input             = @(@{ type = 'message'; role = 'user'; content = 'Run the estate health check and narrate its Findings.' })
        store             = $false
        max_tool_calls    = 2
        max_output_tokens = 4000
        structured_inputs = @{ correlationId = $correlation }
    } | ConvertTo-Json -Depth 10 -Compress
    Invoke-RestMethod -Method Post -Uri "$endpoint/openai/v1/responses" -Headers (Get-FoundryHeader) -ContentType 'application/json' -Body $body -TimeoutSec 180 -SkipHttpErrorCheck
}
function Get-McpCalls($Response) { @($Response.output | Where-Object type -EQ 'mcp_call') }
function Get-Answer($Response) {
    $text = (@($Response.output | Where-Object type -EQ 'message' | ForEach-Object { $_.content } | Where-Object type -EQ 'output_text' | ForEach-Object text) -join '')
    $text | ConvertFrom-Json
}

# Asks until the checks return the Finding ($Expected = 'found') or none ($Expected = 'none'), for up to ten minutes. Resource
# Graph is eventually consistent, so an alert that was just deleted or rebuilt is seen after a short while.
function Wait-ForFindings([string] $Expected) {
    $ids = @()
    for ($i = 0; $i -lt 20; $i++) {
        $response = Invoke-Ask
        if ($response.status -eq 'completed') {
            $checks = @(Get-McpCalls $response | Where-Object name -EQ 'run_checks')
            $ids = @($checks | ForEach-Object { ($_.output | ConvertFrom-Json).findings.id } | Sort-Object)
            if ($Expected -eq 'found' -and $ids -contains 'F-DLQ-ALERT') { return $response }
            if ($Expected -eq 'none' -and $ids.Count -eq 0 -and (Get-McpCalls $response).Count -gt 0) { return $response }
        }
        Start-Sleep -Seconds 30
    }
    Write-Fail "the checks did not return the expected Findings ($Expected) within 10 minutes (last: '$($ids.Count -gt 0 ? $ids -join ',' : 'none')')"
}
function Assert-Calls($Response) {
    if (@(Get-McpCalls $Response | Where-Object name -NE 'run_checks').Count -gt 0) { Write-Fail 'the Agent called a tool other than run_checks' }
    $refused = @(Get-McpCalls $Response | Where-Object { $null -ne $_.error -and $_.error -ne '' -and "$($_.error | ConvertTo-Json -Compress)" -ne '{}' })
    if ($refused.Count -gt 0) { Write-Fail "a tool call was refused: $($refused | ForEach-Object { @{ name = $_.name; error = $_.error } } | ConvertTo-Json -Compress -Depth 5)" }
}

# 2. The alert toggle on: the Finding appears.
Invoke-AsFound as-found
$response = Wait-ForFindings found
Assert-Calls $response
$answer = Get-Answer $response
$explained = @($answer.findings | Where-Object { $_.id -eq 'F-DLQ-ALERT' -and $_.impact -and $_.citation -eq 'r1' })
if (-not ($answer.summary -and $explained.Count -eq 1)) { Write-Fail 'the narration does not explain F-DLQ-ALERT citing r1' }
$subjects = @(Get-McpCalls $response | ForEach-Object { ($_.output | ConvertFrom-Json).findings.evidence.subject })
if ($subjects -notcontains 'carrier-tracking') { Write-Fail "the Finding's evidence does not name carrier-tracking" }
Write-Pass 'with the as-found preset on, the checks return F-DLQ-ALERT among others, naming carrier-tracking, and the real model explains it'
$asFoundResponse = $response.id

# 4 (first part). The hops: API Management's MCP server and the app, with the Agent's identity and the tool name.
function Get-HopCount([string] $Flow) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "AppTraces | where TimeGenerated > ago(1h) | where tostring(Properties.runId) == '$correlation' | where tostring(Properties.flow) == '$Flow' | where tostring(Properties.agentId) == '$identityClient' and tostring(Properties.toolName) == 'run_checks' | count"
    $rows.Count -gt 0 ? [int] $rows[0][0] : 0
}
$hopsPending = 'agent-mcp-health-check', 'agent-view-health-check'

# 3. The toggle off, the estate deployed again: the Finding is gone.
Invoke-AsFound fixed
$response = Wait-ForFindings none
Assert-Calls $response
$answer = Get-Answer $response
if (@($answer.findings).Count -ne 0) { Write-Fail 'the narration explains a Finding although the checks returned none' }
Write-Pass 'with the alert toggle off and the estate deployed again, the checks return no Finding and the narration invents none'

# 4. Ingestion takes minutes: ask again for up to 10.
foreach ($flow in $hopsPending) {
    $count = Wait-Until { $n = Get-HopCount $flow; $n -gt 0 ? $n : $null }
    if (-not $count) { Write-Fail "no hop of $flow on the run $correlation with the Agent's identity and run_checks within 10 minutes" }
    Write-Pass "the Message trail has $count hop(s) of $flow, tagged with the Agent's identity and run_checks"
}

# Nothing kept: store was false. A response the service did keep is deleted.
foreach ($id in $asFoundResponse, $response.id) {
    $kept = (Invoke-WebRequest -Uri "$endpoint/openai/v1/responses/$id" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck).StatusCode
    if ($kept -ne 404) {
        Invoke-WebRequest -Method Delete -Uri "$endpoint/openai/v1/responses/$id" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck | Out-Null
        Write-Host "NOTE  the service kept a response (HTTP $kept) although store was false; it was deleted. Record this in docs/operations/foundry.md (spike 3)."
    }
}

$healthCheck = Get-AppUrl health-check-web -ResourceGroup $resourceGroup
Write-Host ''
Write-Host 'Done: the estate is in the fixed state. Open the page, choose Run health check, and see no Finding:'
Write-Host "  $healthCheck/"
