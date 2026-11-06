#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the P9 Carrier cost Logic App's workflow to the Logic Apps Standard app the estate created, and points it at
    the ERP billing API that Aspire just deployed. The estate can't do this itself: it is deployed before the ERP, and doesn't know its
    address yet.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName

$logicApp = Get-RequiredEnvironmentVariable AZURE_ESTATE_CARRIER_COST_LOGIC_APP 'Deploy the estate first: azd hooks run preprovision'

$erp = Get-AppUrl erp-api -ResourceGroup $resourceGroup
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same address.
Set-AzdEnvironmentValue ESTATE_ERP_BASE_URL $erp

Publish-LogicApp -LogicApp $logicApp -Title 'the Carrier cost Logic App' `
    -Project 'src/Integration/CarrierCostLogicApp' -Include 'host.json', 'parameters.json', 'connections.json', 'carrier-cost' `
    -Settings @{ ERP_BASE_URL = $erp }
Write-Host "The Carrier cost Logic App records Carrier costs with the ERP billing API at $erp."
