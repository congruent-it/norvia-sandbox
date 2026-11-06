#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the E2 Carrier invoice outcome Logic App's workflow and local function (E2.8) to the Logic Apps Standard app the
    estate created, and points it at the ERP billing API and the Partner portal that Aspire just deployed. The estate can't do this
    itself: it is deployed before them, and doesn't know their addresses yet.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID

# The estate has no output left for it (Bicep allows 64), so the app is found by its name.
$logicApp = (Invoke-Az logicapp list --subscription $subscription --resource-group $resourceGroup `
        --query "[?starts_with(name, 'logic-carrier-invoice-outcome-')].name | [0]" --output tsv | Out-String).Trim()
if (-not $logicApp) { Write-Fail 'Deploy the estate first: azd hooks run preprovision' }

$erp = Get-AppUrl erp-api -ResourceGroup $resourceGroup
$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same addresses.
Set-AzdEnvironmentValue ESTATE_ERP_BASE_URL $erp
Set-AzdEnvironmentValue ESTATE_PARTNER_PORTAL_BASE_URL $portal

Publish-LogicApp -LogicApp $logicApp -Title 'the Carrier invoice outcome Logic App' `
    -Project 'src/Integration/CarrierInvoiceOutcomeLogicApp' -Include 'host.json', 'parameters.json', 'connections.json', 'carrier-invoice-outcome', 'lib' `
    -Settings @{ ERP_BASE_URL = $erp; PARTNER_PORTAL_BASE_URL = $portal } `
    -FunctionsProject 'src/Integration/Norvia.Integration.CarrierInvoiceOutcome.Functions'
Write-Host "The Carrier invoice outcome Logic App calls the ERP at $erp and the Carriers' endpoints at $portal."
