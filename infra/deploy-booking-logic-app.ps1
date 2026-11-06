#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the P4 booking Logic App's workflow and local function to the Logic Apps Standard app the
    estate created, and points it at the TMS API and the Partner portal that Aspire just deployed. The estate can't do this
    itself: it is deployed before them, and doesn't know their addresses yet.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName

$logicApp = Get-RequiredEnvironmentVariable AZURE_ESTATE_BOOKING_LOGIC_APP 'Deploy the estate first: azd hooks run preprovision'

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$portal = Get-AppUrl partner-portal-web -ResourceGroup $resourceGroup
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same addresses.
Set-AzdEnvironmentValue ESTATE_TMS_BASE_URL $tms
Set-AzdEnvironmentValue ESTATE_PARTNER_PORTAL_BASE_URL $portal

Publish-LogicApp -LogicApp $logicApp -Title 'the booking Logic App' `
    -Project 'src/Integration/BookingLogicApp' -Include 'host.json', 'parameters.json', 'connections.json', 'carrier-booking', 'lib' `
    -Settings @{ TMS_BASE_URL = $tms; PARTNER_PORTAL_BASE_URL = $portal } `
    -FunctionsProject 'src/Integration/Norvia.Integration.CarrierBooking.Functions'
Write-Host "The booking Logic App calls the TMS at $tms and the Carriers' endpoints at $portal."
