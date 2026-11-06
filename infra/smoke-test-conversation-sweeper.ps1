#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the conversation sweeper (not in CI).
.DESCRIPTION
    Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for (the presenter holds the
    Foundry User role on the project and may manage the resource group), once `azd up` has deployed the estate and the
    postdeploy hooks have deployed the sweeper's workflow:

        infra/smoke-test-conversation-sweeper.ps1

    It leaves a conversation in the Foundry project, sets the sweeper's age to one minute, runs the sweeper once and checks that:
      1. the sweeper's run succeeded, and
      2. the conversation is gone.
    It restores the age (a day) when it ends, however it ends. It prints names, ids and counts only: never a token.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT
$workflow = 'conversation-sweeper'
$trigger = 'Every_night'
$defaultAge = 1440

if (-not $endpoint) { Write-Fail 'no Foundry project endpoint: has the estate been deployed (azd provision)?' }
$logicApp = (Invoke-Az resource list --subscription $subscription --resource-group $resourceGroup --resource-type Microsoft.Web/sites `
        --query "[?tags.workload=='conversation-sweeper'].name | [0]" --output tsv | Out-String).Trim()
if (-not $logicApp) { Write-Fail "there is no conversation sweeper in $resourceGroup" }
$state = (Invoke-Az webapp show --subscription $subscription --resource-group $resourceGroup --name $logicApp --query state --output tsv | Out-String).Trim()
if ($state -ne 'Running') { Write-Fail "the sweeper $logicApp is $state, not Running (the as-found toggle noConversationSweeper is set?)" }

function Set-SweeperAge([int] $Minutes) {
    Invoke-Az logicapp config appsettings set --subscription $subscription --name $logicApp --resource-group $resourceGroup `
        --settings "CONVERSATION_MAX_AGE_MINUTES=$Minutes" --output none | Out-Null
}

function Get-ConversationStatus([string] $Id) {
    $headers = @{ Authorization = "Bearer $(Get-AzAccessToken https://ai.azure.com)" }
    (Invoke-WebRequest -Uri "$endpoint/openai/v1/conversations/$Id" -Headers $headers -TimeoutSec 60 -SkipHttpErrorCheck).StatusCode
}

try {
    # Leave a conversation behind.
    $headers = @{ Authorization = "Bearer $(Get-AzAccessToken https://ai.azure.com)" }
    $created = Invoke-WebRequest -Method Post -Uri "$endpoint/openai/v1/conversations" -Headers $headers -ContentType 'application/json' `
        -Body '{"metadata":{"smoke":"conversation-sweeper"}}' -TimeoutSec 60 -SkipHttpErrorCheck
    $conversation = $created.StatusCode -lt 300 ? ($created.Content | ConvertFrom-Json).id : $null
    if (-not $conversation) { Write-Fail 'could not create a conversation: does the presenter hold the Foundry User role?' }
    if ((Get-ConversationStatus $conversation) -ne 200) { Write-Fail "the conversation $conversation cannot be read back" }
    Write-Pass "left the conversation $conversation in the project"

    # A minute old, then sweep. A changed app setting restarts the app, so give it time before the trigger is run.
    Set-SweeperAge 1
    Write-Host 'Waiting 90 seconds for the conversation to be a minute old and the sweeper to restart...'
    Start-Sleep -Seconds 90

    $management = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.Web/sites/$logicApp/hostruntime/runtime/webhooks/workflow/api/management/workflows/$workflow"
    $started = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        if ($null -ne (Invoke-Az rest --method post --url "$management/triggers/$trigger/run?api-version=2018-11-01" --output none -AllowFailure)) { break }
        if ($attempt -ge 5) { Write-Fail "could not run the sweeper's trigger" }
        Start-Sleep -Seconds 30
    }

    # The run this call started: the newest run since the call.
    $runStatus = ''
    for ($i = 1; $i -le 30; $i++) {
        $runStatus = ((Invoke-Az rest --method get --url "$management/runs?api-version=2018-11-01" `
                    --query "value[?properties.startTime >= '$started'] | [0].properties.status" --output tsv -AllowFailure) | Out-String).Trim()
        if ($runStatus -in 'Succeeded', 'Failed', 'Cancelled') { break }
        Start-Sleep -Seconds 10
    }
    if ($runStatus -ne 'Succeeded') { Write-Fail "the sweeper's run ended as '$($runStatus ? $runStatus : 'no run')', not Succeeded" }
    Write-Pass "the sweeper's run succeeded"

    if ((Get-ConversationStatus $conversation) -ne 404) { Write-Fail "the conversation $conversation is still there after the sweep" }
    Write-Pass "the conversation $conversation is gone"
}
finally {
    Set-SweeperAge $defaultAge
}
