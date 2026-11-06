#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy step: lets Entra send a user back to the app they signed in to at its Easy Auth.
.DESCRIPTION
    The presenter to the demo UIs (ADR 0024), an Integration operator to the Integration console (ADR 0026) and to the
    Estate health check (#392), a Driver to the Driver app (ADR 0037), the presenter to the Simulator (ADR 0042),
    Finance to the ERP. The estate can't do this itself: it is deployed before the apps, whose addresses it doesn't know
    yet. The container apps whose Easy Auth signs in to an app registration are the ones the AppHost put behind it.
#>
$ErrorActionPreference = 'Stop'
$PSNativeCommandArgumentPassing = 'Standard'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID

# Every container app with Easy Auth: its name, the client ID it signs in to and its host name.
$signedInApps = foreach ($app in @(Invoke-Az containerapp list --subscription $subscription --resource-group $resourceGroup `
            --query '[].name' --output tsv) | ForEach-Object { "$_".Trim() } | Where-Object { $_ }) {
    $clientId = (Invoke-Az containerapp auth show --subscription $subscription --name $app --resource-group $resourceGroup `
            --query identityProviders.azureActiveDirectory.registration.clientId --output tsv -AllowFailure | Out-String).Trim()
    if (-not $clientId) { continue }
    $hostName = (Invoke-Az containerapp show --subscription $subscription --name $app --resource-group $resourceGroup `
            --query properties.configuration.ingress.fqdn --output tsv | Out-String).Trim()
    [pscustomobject]@{ App = $app; ClientId = $clientId; HostName = $hostName }
}

function Register-SignInCallbacks {
    param(
        [Parameter(Mandatory)][string] $ClientId,
        [Parameter(Mandatory)][string] $What,
        [Parameter(Mandatory)][string] $KeptAs,
        [switch] $Optional
    )
    $redirectUris = @()
    foreach ($app in @($signedInApps) | Where-Object { $_.ClientId -eq $ClientId }) {
        # Easy Auth's callback for Entra.
        $redirectUris += "https://$($app.HostName)/.auth/login/aad/callback"
        Write-Host "Open $($app.App) at https://$($app.HostName)"
    }
    # The Driver app signs in to the demo UIs' registration instead when the Driver picker is on (ADR 0047), so nothing
    # signs in to its own.
    if (-not $redirectUris -and $Optional) {
        Write-Host "No container app signs in to $What's app registration $ClientId; skipping it."
        return
    }
    if (-not $redirectUris) { Write-Fail "No container app signs in to $What's app registration $ClientId." }

    # Kept in the azd environment too, so deploying the estate again keeps the redirect URIs.
    & azd env set $KeptAs ($redirectUris -join ',')
    if ($LASTEXITCODE -ne 0) { Write-Fail "azd env set $KeptAs failed with exit code $LASTEXITCODE" }
    Invoke-Az ad app update --id $ClientId --web-redirect-uris @redirectUris | Out-Null
}

function Get-RequiredClientId {
    param([Parameter(Mandatory)][string] $Variable)
    $value = [Environment]::GetEnvironmentVariable($Variable)
    if (-not $value) { Write-Fail "$Variable is not set. Deploy the estate first: azd hooks run preprovision" }
    $value
}

Register-SignInCallbacks -ClientId (Get-RequiredClientId AZURE_ESTATE_DEMO_UIS_CLIENT_ID) `
    -What 'the demo UIs' -KeptAs ESTATE_DEMO_UI_REDIRECT_URIS
Register-SignInCallbacks -ClientId (Get-RequiredClientId AZURE_ESTATE_INTEGRATION_CONSOLE_CLIENT_ID) `
    -What 'the Integration console' -KeptAs ESTATE_INTEGRATION_CONSOLE_REDIRECT_URIS
Register-SignInCallbacks -ClientId (Get-RequiredClientId AZURE_ESTATE_HEALTH_CHECK_CLIENT_ID) `
    -What 'the Estate health check' -KeptAs ESTATE_HEALTH_CHECK_REDIRECT_URIS
Register-SignInCallbacks -ClientId (Get-RequiredClientId AZURE_ESTATE_DRIVER_APP_REGISTRATION_CLIENT_ID) `
    -What 'the Driver app' -KeptAs ESTATE_DRIVER_APP_REDIRECT_URIS -Optional
Register-SignInCallbacks -ClientId (Get-RequiredClientId AZURE_ESTATE_SIMULATOR_REGISTRATION_CLIENT_ID) `
    -What 'the Simulator' -KeptAs ESTATE_SIMULATOR_REDIRECT_URIS
Register-SignInCallbacks -ClientId (Get-RequiredClientId AZURE_ESTATE_ERP_REGISTRATION_CLIENT_ID) `
    -What 'the ERP' -KeptAs ESTATE_ERP_REDIRECT_URIS
