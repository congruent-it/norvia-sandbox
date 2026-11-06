#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the E1 Shipper invoice Logic App's workflow and local function to the Logic Apps Standard app the
    estate created, and points it at the ERP billing API and the Partner portal that Aspire just deployed. The estate can't do this
    itself: it is deployed before them, and doesn't know their addresses yet.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName

$logicApp = Get-RequiredEnvironmentVariable AZURE_ESTATE_SHIPPER_INVOICE_LOGIC_APP 'Deploy the estate first: azd hooks run preprovision'

$erp = Get-AppUrl erp-api -ResourceGroup $resourceGroup
$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same addresses.
Set-AzdEnvironmentValue ESTATE_ERP_BASE_URL $erp
Set-AzdEnvironmentValue ESTATE_PARTNER_PORTAL_BASE_URL $portal

Publish-LogicApp -LogicApp $logicApp -Title 'the Shipper invoice Logic App' `
    -Project 'src/Integration/ShipperInvoiceLogicApp' -Include 'host.json', 'parameters.json', 'connections.json', 'shipper-invoice', 'lib' `
    -Settings @{ ERP_BASE_URL = $erp; PARTNER_PORTAL_BASE_URL = $portal } `
    -FunctionsProject 'src/Integration/Norvia.Integration.ShipperInvoice.Functions'
Write-Host "The Shipper invoice Logic App calls the ERP at $erp and the Shippers' endpoints at $portal."
