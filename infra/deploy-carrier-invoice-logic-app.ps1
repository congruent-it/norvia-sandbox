#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the Carrier invoice Logic App's workflow and local functions (E2.6) to the Logic Apps Standard app
    the estate created, and points it at the ERP billing API that Aspire just deployed. The estate can't do this itself: it is
    deployed before the ERP, whose address it doesn't know yet.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID

# The estate has no output left for it (Bicep allows 64), so the app is found by its name.
$logicApp = (Invoke-Az logicapp list --subscription $subscription --resource-group $resourceGroup `
        --query "[?starts_with(name, 'logic-carrier-invoice-')].name | [0]" --output tsv | Out-String).Trim()
if (-not $logicApp) { Write-Fail 'Deploy the estate first: azd hooks run preprovision' }

$erp = Get-AppUrl erp-api -ResourceGroup $resourceGroup
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same address.
Set-AzdEnvironmentValue ESTATE_ERP_BASE_URL $erp

Publish-LogicApp -LogicApp $logicApp -Title 'the Carrier invoice Logic App' `
    -Project 'src/Integration/CarrierInvoiceLogicApp' -Include 'host.json', 'parameters.json', 'carrier-invoice', 'lib' `
    -Settings @{ ERP_BASE_URL = $erp } `
    -FunctionsProject 'src/Integration/Norvia.Integration.CarrierInvoice.Functions'
Write-Host "The Carrier invoice Logic App receives Carrier invoices with the ERP billing API at $erp."
