#Requires -Version 7.4
<#
.SYNOPSIS
    Makes an Entra test user for each active Driver of the seed file, and assigns it the app role of the Driver app's
    registration that carries the Driver ID (ADR 0037).
.DESCRIPTION
    Run by provision-estate.ps1 after the estate is deployed, with the Driver app's client ID. It is safe to run again: it
    finds the users it made and keeps the roles it assigned.

    A user's name is the Driver ID lowercased at the tenant's default domain, such as drv-0001@norvia.onmicrosoft.com.
    They share one password, made once and kept in the azd environment, which the presenter panel shows (the users hold
    no access to anything but the Driver app, and only through its roles). The domain and the password go to the apps as
    the Estate parameters azd asks for.

    It needs the rights to create users and assign app roles in the tenant, as the estate's Entra parts do.
.PARAMETER ClientId
    The client ID of the Driver app registration.
#>
[CmdletBinding()]
param([Parameter(Mandatory, Position = 0)][string] $ClientId)
$ErrorActionPreference = 'Stop'
$PSNativeCommandArgumentPassing = 'Standard'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$seed = Join-Path $PSScriptRoot '../seed/norvia.seed.json'
$graph = 'https://graph.microsoft.com/v1.0'

$domain = (Invoke-Az rest --method get --url "$graph/organization" `
        --query 'value[0].verifiedDomains[?isDefault].name | [0]' --output tsv | Out-String).Trim()
if (-not $domain) { Write-Fail "The tenant has no default domain to make the Drivers' test users in." }

# Made once, so the password the presenter panel shows stays the one the users have. The fixed parts satisfy Entra's complexity rules.
$password = $env:ESTATE_DRIVER_SIGN_IN_PASSWORD
if (-not $password) {
    $random = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(12)).ToLowerInvariant()
    $password = "Nv-$random-Aa1"
    Set-AzdEnvironmentValue ESTATE_DRIVER_SIGN_IN_PASSWORD $password
}

$servicePrincipal = (Invoke-Az ad sp show --id $ClientId --query id --output tsv | Out-String).Trim()
$roles = Invoke-Az ad app show --id $ClientId --query appRoles --output json | Out-String | ConvertFrom-Json

foreach ($driver in @((Get-Content -Path $seed -Raw | ConvertFrom-Json).drivers) | Where-Object { $_.active }) {
    $id = $driver.id
    $nickname = $id.ToLowerInvariant()
    $userName = "$nickname@$domain"

    $userId = (Invoke-Az ad user show --id $userName --query id --output tsv -AllowFailure | Out-String).Trim()
    if ($userId) {
        Invoke-Az ad user update --id $userId --password $password --force-change-password-next-sign-in false --output none | Out-Null
    }
    else {
        $userId = (Invoke-Az ad user create --display-name $driver.name --user-principal-name $userName --mail-nickname $nickname `
                --password $password --force-change-password-next-sign-in false --query id --output tsv | Out-String).Trim()
    }

    $roleId = @($roles | Where-Object { $_.value -eq $id }).id | Select-Object -First 1
    if (-not $roleId) { Write-Fail "The Driver app has no app role $id. Deploy the estate again." }

    $assignments = Invoke-AzRest -Method get -Url "$graph/users/$userId/appRoleAssignments"
    $assigned = @($assignments.value | Where-Object { $_.resourceId -eq $servicePrincipal -and $_.appRoleId -eq $roleId })
    if ($assigned.Count -eq 0) {
        Invoke-AzRest -Method post -Url "$graph/users/$userId/appRoleAssignments" `
            -Body @{ principalId = $userId; resourceId = $servicePrincipal; appRoleId = $roleId } | Out-Null
    }
    Write-Host "Driver $id signs in as $userName"
}

# The Estate parameters, as provision-estate.ps1 sets the estate's outputs.
function Set-EstateParameter {
    param([string] $Name, [string] $Value)
    Set-AzdEnvironmentValue "AZURE_$Name" $Value
    & azd env config set "infra.parameters.$($Name.ToLowerInvariant())" (ConvertTo-Json -InputObject $Value -Compress)
    if ($LASTEXITCODE -ne 0) { Write-Fail "azd env config set infra.parameters.$($Name.ToLowerInvariant()) failed with exit code $LASTEXITCODE" }
}
Set-EstateParameter ESTATE_DRIVER_SIGN_IN_DOMAIN $domain
Set-EstateParameter ESTATE_DRIVER_SIGN_IN_PASSWORD $password
