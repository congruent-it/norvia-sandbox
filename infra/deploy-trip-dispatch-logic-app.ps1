#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: deploys the P11 Trip dispatch Logic App's workflow to the Logic Apps Standard app the estate created, and
    points it at the TMS API and the Driver app that Aspire just deployed. The estate can't do this itself: it is deployed before
    them, and doesn't know their addresses yet.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName

$logicApp = Get-RequiredEnvironmentVariable AZURE_ESTATE_TRIP_DISPATCH_LOGIC_APP 'Deploy the estate first: azd hooks run preprovision'

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$driverApp = Get-AppUrl driver-app-api -ResourceGroup $resourceGroup
# Kept in the azd environment too, so deploying the estate again gives the Logic App the same addresses.
Set-AzdEnvironmentValue ESTATE_TMS_BASE_URL $tms
Set-AzdEnvironmentValue ESTATE_DRIVER_APP_BASE_URL $driverApp

Publish-LogicApp -LogicApp $logicApp -Title 'the Trip dispatch Logic App' `
    -Project 'src/Integration/TripDispatchLogicApp' -Include 'host.json', 'parameters.json', 'connections.json', 'trip-dispatch' `
    -Settings @{ TMS_BASE_URL = $tms; DRIVER_APP_BASE_URL = $driverApp }
Write-Host "The Trip dispatch Logic App calls the TMS at $tms and the Driver app at $driverApp."
