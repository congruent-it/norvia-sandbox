#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the conversation sweeper's workflow to the Logic Apps Standard app the estate created. Unlike the other Logic
    Apps it needs no address from the apps Aspire deploys: it calls only Foundry, whose project endpoint the estate already gave it.
    The estate is at its 64 outputs, so the app is found by its tag instead of by an output.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID

$logicApp = (Invoke-Az resource list --subscription $subscription --resource-group $resourceGroup --resource-type Microsoft.Web/sites `
        --query "[?tags.workload=='conversation-sweeper'].name | [0]" --output tsv | Out-String).Trim()
if (-not $logicApp) { Write-Fail "No conversation sweeper in $resourceGroup. Deploy the estate first: azd hooks run preprovision" }
$enabled = (Invoke-Az resource show --subscription $subscription --resource-group $resourceGroup --name $logicApp `
        --resource-type Microsoft.Web/sites --query properties.enabled --output tsv | Out-String).Trim()
if ($enabled -eq 'false') {
    Write-Host "The conversation sweeper $logicApp is disabled (the as-found toggle noConversationSweeper), so its workflow is not deployed."
    return
}

Publish-LogicApp -LogicApp $logicApp -Title 'the conversation sweeper' `
    -Project 'src/Integration/ConversationSweeperLogicApp' -Include 'host.json', 'parameters.json', 'connections.json', 'conversation-sweeper'
Write-Host "The conversation sweeper deletes the conversations and responses of the Foundry project every night."
