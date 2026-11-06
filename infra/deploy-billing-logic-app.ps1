#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the E1 Billing Logic App's workflow to the Logic Apps Standard app the estate created, and points it at
    the ERP billing API that Aspire just deployed. The estate can't do this itself: it is deployed before the ERP, and doesn't know its
    address yet.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName

$logicApp = Get-RequiredEnvironmentVariable AZURE_ESTATE_BILLING_LOGIC_APP 'Deploy the estate first: azd hooks run preprovision'

$erp = Get-AppUrl erp-api -ResourceGroup $resourceGroup
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same address.
Set-AzdEnvironmentValue ESTATE_ERP_BASE_URL $erp

Publish-LogicApp -LogicApp $logicApp -Title 'the Billing Logic App' `
    -Project 'src/Integration/BillingLogicApp' -Include 'host.json', 'parameters.json', 'connections.json', 'billing' `
    -Settings @{ ERP_BASE_URL = $erp }
Write-Host "The Billing Logic App records Billable lines with the ERP billing API at $erp."
