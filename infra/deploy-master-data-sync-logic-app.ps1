#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the E3 master-data sync Logic App's workflow to the Logic Apps Standard app the estate created, and
    points it at the TMS API that Aspire just deployed. The estate can't do this itself: it is deployed before the TMS, and doesn't know
    its address yet.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName

$logicApp = Get-RequiredEnvironmentVariable AZURE_ESTATE_MASTER_DATA_SYNC_LOGIC_APP 'Deploy the estate first: azd hooks run preprovision'

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same address.
Set-AzdEnvironmentValue ESTATE_TMS_BASE_URL $tms

Publish-LogicApp -LogicApp $logicApp -Title 'the master-data sync Logic App' `
    -Project 'src/Integration/MasterDataSyncLogicApp' -Include 'host.json', 'parameters.json', 'connections.json', 'master-data-sync' `
    -Settings @{ TMS_BASE_URL = $tms }
Write-Host "The master-data sync Logic App replaces master data in the TMS API at $tms."
