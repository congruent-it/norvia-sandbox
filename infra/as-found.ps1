#Requires -Version 7.4
<#
.SYNOPSIS
    Sets the estate's as-found toggles and deploys the estate with them (docs/operations/as-found.md).
.DESCRIPTION
      infra/as-found.ps1 fixed                                  every alert, and a receiver
      infra/as-found.ps1 as-found                               the preset in infra/presets/as-found.json
      infra/as-found.ps1 noAlertReceiver=true                   one toggle, the others as they are
      infra/as-found.ps1 flowsWithoutFailedRunAlert=a,b         a list toggle takes a comma-separated list, empty for none

    It stores the toggles in the azd environment as ESTATE_ASFOUND, runs the estate deployment alone (azd's preprovision
    hook, not the apps), deploys again the Logic Apps whose workflow a toggle changes (tracked properties, the Driver app report),
    sets the TMS Container App's settings for the business safety-net toggles, sets every application's log level for chattyTelemetry,
    and prints what changed. The TMS's
    lifecycle rule (proofFilesStayHot) is Aspire's, so it changes at the next azd provision; the script says so.
.PARAMETER Setting A preset (the name of a file in infra/presets) or toggle=value.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Setting)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$presets = Join-Path $PSScriptRoot 'presets'
$fixed = Get-Content -Path (Join-Path $presets 'fixed.json') -Raw | ConvertFrom-Json -AsHashtable

function Write-Usage {
    [Console]::Error.WriteLine('Usage: infra/as-found.ps1 <preset|toggle=value>')
    [Console]::Error.WriteLine("Presets: $((Get-ChildItem -Path $presets -Filter '*.json').BaseName -join ' ')")
    [Console]::Error.WriteLine("Toggles: $((ConvertTo-OrderedToggle $fixed).Keys -join ' ')")
}

# The toggles with their keys in order, as `jq -S` writes them.
function ConvertTo-OrderedToggle([Collections.IDictionary] $Toggles) {
    $sorted = [ordered]@{}
    $keys = [string[]] $Toggles.Keys
    [Array]::Sort($keys, [StringComparer]::Ordinal)
    foreach ($key in $keys) { $sorted[$key] = $Toggles[$key] }
    $sorted
}

function ConvertTo-ToggleJson($Value) { ConvertTo-Json -InputObject $Value -Compress -Depth 10 }

# A toggle that is absent is off (a list is empty).
function Get-Toggle([Collections.IDictionary] $Toggles, [string] $Name) {
    $Toggles.Contains($Name) ? $Toggles[$Name] : $false
}

if (-not $Setting) { Write-Usage; exit 2 }

$currentJson = & azd env get-value ESTATE_ASFOUND 2>$null
$current = $null
if ($LASTEXITCODE -eq 0 -and $currentJson) {
    try { $current = ($currentJson | Out-String) | ConvertFrom-Json -AsHashtable } catch { $current = $null }
}
$current = ConvertTo-OrderedToggle ($current ?? $fixed)

if ($Setting.Contains('=')) {
    $toggle, $value = $Setting -split '=', 2
    if (-not $fixed.Contains($toggle)) { [Console]::Error.WriteLine("$toggle is not a toggle."); Write-Usage; exit 2 }
    $updated = ConvertTo-OrderedToggle $current
    if ($fixed[$toggle] -is [bool]) {
        if ($value -cne 'true' -and $value -cne 'false') { [Console]::Error.WriteLine("$toggle takes true or false."); exit 2 }
        $updated[$toggle] = $value -ceq 'true'
    }
    elseif ($fixed[$toggle] -is [array]) {
        $updated[$toggle] = @($value -split ',' | Where-Object { $_ -ne '' })
    }
    else { [Console]::Error.WriteLine("$toggle is not a toggle."); Write-Usage; exit 2 }
}
else {
    $preset = Join-Path $presets "$Setting.json"
    if (-not (Test-Path $preset -PathType Leaf)) { [Console]::Error.WriteLine("$Setting is not a preset."); Write-Usage; exit 2 }
    $updated = Get-Content -Path $preset -Raw | ConvertFrom-Json -AsHashtable
}
$updated = ConvertTo-OrderedToggle $updated

Set-AzdEnvironmentValue ESTATE_ASFOUND (ConvertTo-ToggleJson $updated)
Write-Host 'Deploying the estate with the as-found toggles...'
& azd hooks run preprovision
if ($LASTEXITCODE -ne 0) { Write-Fail "azd hooks run preprovision failed with exit code $LASTEXITCODE" }

$before = @(Get-Toggle $current 'logicAppsWithoutTrackedProperties')
$after = @(Get-Toggle $updated 'logicAppsWithoutTrackedProperties')
$before = $before | Where-Object { $_ -is [string] }
$after = $after | Where-Object { $_ -is [string] }
$flowsChanged = @(@($before | Where-Object { $_ -notin $after }) + @($after | Where-Object { $_ -notin $before }))
if ((Get-Toggle $current 'noReportBackToDriverApp') -ne (Get-Toggle $updated 'noReportBackToDriverApp')) { $flowsChanged += 'driver-reports' }

if ($flowsChanged.Count -gt 0) {
    # The tracked properties are in the workflow, which the deploy scripts publish (infra/stage-logic-app.ps1).
    Import-AzdEnvironment
    foreach ($flow in $flowsChanged) {
        $pattern = "-Include\s.*'$([regex]::Escape($flow))'(,\s*'lib')?\s*``?\s*$"
        $deployScript = Get-ChildItem -Path $PSScriptRoot -Filter 'deploy-*-logic-app.ps1' |
            Where-Object { Select-String -Path $_.FullName -Pattern $pattern -Quiet } | Select-Object -First 1
        if (-not $deployScript) { Write-Fail "No deploy script for the flow $flow." }
        Write-Host "Deploying the $flow Logic App again ($($deployScript.Name))..."
        & $deployScript.FullName
    }
}

# The business safety nets are settings of the TMS's Container App. Aspire sets them at the next deploy from the same toggles; this applies
# them to the running app now, as a new revision.
$settingNames = [ordered]@{
    staleThresholdsUnset       = 'ShipmentStale__ThresholdsSet'
    proofMissingThresholdUnset = 'ProofsOfDelivery__MissingThresholdSet'
    lateDeliveryCheckOff       = 'LateDelivery__CheckEnabled'
}
$settings = @(foreach ($toggle in $settingNames.Keys) {
        if ((Get-Toggle $current $toggle) -ne (Get-Toggle $updated $toggle)) {
            "$($settingNames[$toggle])=$((Get-Toggle $updated $toggle) ? 'false' : 'true')"
        }
    })
if ($settings.Count -gt 0) {
    Import-AzdEnvironment
    Write-Host "Setting the TMS API's business safety nets..."
    Invoke-Az containerapp update --subscription $env:AZURE_SUBSCRIPTION_ID --name tms-api --resource-group (Get-ResourceGroupName) `
        --set-env-vars @settings --output none | Out-Null
}
# chattyTelemetry is the applications' log level, a setting of each Container App. Aspire sets it at the next deploy from the same toggle;
# this applies it to the running apps now, as a new revision each.
if ((Get-Toggle $current 'chattyTelemetry') -ne (Get-Toggle $updated 'chattyTelemetry')) {
    $logLevel = (Get-Toggle $updated 'chattyTelemetry') ? 'Debug' : 'Information'
    Import-AzdEnvironment
    foreach ($app in (Get-LoggingContainerApp)) {
        Write-Host "Setting $app's log level to $logLevel..."
        Invoke-Az containerapp update --subscription $env:AZURE_SUBSCRIPTION_ID --name $app --resource-group (Get-ResourceGroupName) `
            --set-env-vars "Logging__LogLevel__Default=$logLevel" --output none | Out-Null
    }
}
foreach ($toggle in 'proofFilesStayHot', 'proofFilesNeverDeleted') {
    if ((Get-Toggle $current $toggle) -ne (Get-Toggle $updated $toggle)) {
        Write-Host "$toggle changed: the TMS's storage account is Aspire's, so run azd provision to apply it."
    }
}

Write-Host 'Changed:'
$changes = @(foreach ($key in $updated.Keys) {
        $was = $current.Contains($key) ? $current[$key] : $null
        if ((ConvertTo-ToggleJson $was) -cne (ConvertTo-ToggleJson $updated[$key])) {
            "  ${key}: $(ConvertTo-ToggleJson $was) -> $(ConvertTo-ToggleJson $updated[$key])"
        }
    })
Write-Host ($changes.Count -gt 0 ? ($changes -join [Environment]::NewLine) : '  nothing, the toggles were already set')
Write-Host 'Now:'
foreach ($key in $updated.Keys) { Write-Host "  ${key}: $(ConvertTo-ToggleJson $updated[$key])" }
# The caller (Invoke-AsFound) reads the exit code, which is that of the last native command otherwise.
exit 0
