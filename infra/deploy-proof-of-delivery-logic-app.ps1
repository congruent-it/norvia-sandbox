#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the Carrier Proof of delivery Logic App's workflow and local function (P6.8) to the Logic Apps Standard app
    the estate created, and points it at the TMS API that Aspire just deployed. The estate can't do this itself: it is
    deployed before the TMS, whose address it doesn't know yet.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName

$logicApp = Get-RequiredEnvironmentVariable AZURE_ESTATE_PROOF_OF_DELIVERY_LOGIC_APP 'Deploy the estate first: azd hooks run preprovision'

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same address.
Set-AzdEnvironmentValue ESTATE_TMS_BASE_URL $tms

Publish-LogicApp -LogicApp $logicApp -Title 'the Carrier Proof of delivery Logic App' `
    -Project 'src/Integration/ProofOfDeliveryLogicApp' -Include 'host.json', 'parameters.json', 'connections.json', 'carrier-proof-of-delivery', 'lib' `
    -Settings @{ TMS_BASE_URL = $tms } `
    -FunctionsProject 'src/Integration/Norvia.Integration.CarrierProofOfDelivery.Functions'
Write-Host "The Carrier Proof of delivery Logic App calls the TMS at $tms."
