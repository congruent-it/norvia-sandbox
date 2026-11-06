#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the Foundry estate (not in CI).
.DESCRIPTION
    Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for (the presenter holds the
    Foundry User role on the project), once `azd up` has deployed the estate:

        infra/smoke-test-foundry.ps1

    It calls the shared model once through the project endpoint and checks that:
      1. the model deployment is DataZoneStandard, in an EU region: processing stays in the EU Data Boundary, never GlobalStandard;
      2. the deployment is capped and the account is tagged for cost;
      3. the model answers one request;
      4. Foundry's span for the call reaches Application Insights, and the prompt text (a sentinel the script made up) is nowhere in
         the telemetry tables the Message trails use. The prompt content is allowed in the protected AppGenAIContent table only
         (docs/operations/foundry.md, spike 2).
    It prints names and counts only: never a prompt or a token.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT

if (-not $endpoint) { Write-Fail 'no Foundry project endpoint: has the estate been deployed (azd provision)?' }
$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
if (-not $account) { Write-Fail "there is no Foundry account in $resourceGroup" }

# 1 and 2. The deployment: type, region, cap, tags.
$deployment = Invoke-Az cognitiveservices account deployment list --resource-group $resourceGroup --name $account --query '[0]' --output json | Out-String | ConvertFrom-Json
if ($null -eq $deployment) { Write-Fail 'the Foundry account has no model deployment (foundryModelDeployed is off?)' }
$model = $deployment.name
$sku = $deployment.sku.name
$capacity = [int] $deployment.sku.capacity
if ($sku -ne 'DataZoneStandard') { Write-Fail "the deployment $model is $sku, not DataZoneStandard" }
$region = (Invoke-Az cognitiveservices account show --resource-group $resourceGroup --name $account --query location --output tsv | Out-String).Trim()
$euRegions = 'francecentral', 'germanywestcentral', 'italynorth', 'norwayeast', 'polandcentral', 'spaincentral', 'swedencentral', 'switzerlandnorth', 'westeurope'
if ($region -notin $euRegions) { Write-Fail "the Foundry account is in $region, which is not an EU region" }
Write-Pass "the deployment $model is DataZoneStandard in $region"

if ($capacity -gt 100) { Write-Fail "the deployment $model is capped at $capacity, not 100 or less" }
$tag = (Invoke-Az cognitiveservices account show --resource-group $resourceGroup --name $account --query 'tags.workload' --output tsv | Out-String).Trim()
if ($tag -ne 'foundry-agents') { Write-Fail 'the Foundry account has no workload cost tag' }
Write-Pass "the deployment is capped at $capacity thousand tokens a minute, and the account is tagged for cost"

# 3. One request through the project endpoint, as the signed-in presenter.
$sentinel = "norvia-smoke-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())-$(Get-RandomSuffix)"
$token = Get-AzAccessToken https://ai.azure.com
$body = @{ model = $model; input = "Reply with the single word pong. Reference $sentinel" } | ConvertTo-Json -Compress
$answer = try {
    (Invoke-WebRequest -Method Post -Uri "$endpoint/openai/v1/responses" -Headers @{ Authorization = "Bearer $token" } `
            -ContentType 'application/json' -Body $body -TimeoutSec 120 -SkipHttpErrorCheck).StatusCode
}
catch { 0 }
if ($answer -ne 200) { Write-Fail "the model answered HTTP $answer, not 200: does the presenter hold the Foundry User role, and is the quota used up?" }
Write-Pass 'the model answered one request'

# 4. The span reached Application Insights, and the prompt text did not.
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup
function Get-Count([string] $Query) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace -Timespan PT1H $Query
    $rows.Count -gt 0 ? [int] $rows[0][0] : 0
}
# Ingestion takes a few minutes, so the check asks again, for up to 10 minutes.
$spans = Wait-Until { $count = Get-Count "union AppDependencies, AppTraces | where TimeGenerated > ago(30m) | where Properties has 'gen_ai' | count"; $count -gt 0 ? $count : $null }
if (-not $spans) { Write-Fail 'no Foundry span reached Application Insights within 10 minutes: is the project connected to it?' }
$leaked = Get-Count "union AppDependencies, AppTraces, AppEvents, AppRequests | where TimeGenerated > ago(30m) | where Properties has '$sentinel' or Name has '$sentinel' or Message has '$sentinel' | count"
if ($leaked -ne 0) { Write-Fail "the prompt text is in $leaked row(s) of the telemetry the Message trails use: content recording is on there" }
Write-Pass "$spans Foundry span(s) reached Application Insights, and the prompt text is in none of the Message trail tables"
