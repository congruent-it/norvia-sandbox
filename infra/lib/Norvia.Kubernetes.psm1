#Requires -Version 7.4
<#
.SYNOPSIS
    The helpers of the Kubernetes smoke script (ADR 0048, ADR 0053): local tokens, ingress URLs, waiting and named steps.
.DESCRIPTION
    Import it beside Norvia.Infra.psm1, whose Write-Pass and Write-Fail it uses:
        Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Kubernetes.psm1') -Force
    Nothing here needs a cluster; the script reads the cluster's Secrets itself.
#>

Set-StrictMode -Version Latest

function ConvertTo-Base64Url {
    <# .SYNOPSIS Base64url, as a JWT writes its parts. #>
    param([Parameter(Mandatory)][byte[]] $Bytes)
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-LocalToken {
    <#
    .SYNOPSIS A token signed as the local stand-ins sign theirs (src/Norvia.AppHost/LocalTokens.cs): HS256, valid for an hour.
    .DESCRIPTION
        Locally there is no Entra, so the apps trust tokens signed with one key. The chart keeps it in the Secret local-tokens, key
        signing-key, as a base64 string that the apps decode into the key's bytes. -SigningKey is that string.
    .PARAMETER ClientId The client in azp: a Persona's (norvia-local-persona-<id>) or an identity's (norvia-local-simulator).
    .PARAMETER Roles The app roles. With none, the claim is left out, as Entra leaves it out.
    #>
    param(
        [Parameter(Mandatory)][string] $SigningKey,
        [Parameter(Mandatory)][string] $Issuer,
        [Parameter(Mandatory)][string] $Audience,
        [Parameter(Mandatory)][string] $ClientId,
        [string[]] $Roles = @()
    )
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $payload = [ordered]@{ iss = $Issuer; aud = $Audience; azp = $ClientId; iat = $now; nbf = $now; exp = $now + 3600 }
    if ($Roles.Count -gt 0) { $payload.roles = $Roles }
    $encode = { param($Object) ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(($Object | ConvertTo-Json -Compress))) }
    $signed = "$(& $encode ([ordered]@{ alg = 'HS256'; typ = 'JWT' })).$(& $encode $payload)"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Convert]::FromBase64String($SigningKey))
    try { "$signed.$(ConvertTo-Base64Url $hmac.ComputeHash([Text.Encoding]::ASCII.GetBytes($signed)))" }
    finally { $hmac.Dispose() }
}

function Get-IngressUrl {
    <# .SYNOPSIS The URL of an application's Ingress: <application>.<base domain>, as templates/ingress.yaml names it. #>
    param(
        [Parameter(Mandatory)][string] $Application,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $BaseDomain,
        [switch] $Insecure
    )
    "$($Insecure ? 'http' : 'https')://$Application.$BaseDomain"
}

function Test-Status {
    <# .SYNOPSIS Whether a status is the expected one, ignoring case, spaces and hyphens ("In transit" is what the TMS may spell InTransit). #>
    param($Actual, [Parameter(Mandatory)][string] $Expected)
    if ($null -eq $Actual) { return $false }
    $normalise = { param($Text) ([string] $Text) -replace '[\s\-_]', '' }
    (& $normalise $Actual) -ieq (& $normalise $Expected)
}

function Wait-ForValue {
    <#
    .SYNOPSIS Runs a condition until it returns something, and returns that. Running out fails, naming what was waited for.
    .PARAMETER Describe Runs once when the attempts are used up and says what was last seen, so the failure holds it.
    #>
    param(
        [Parameter(Mandatory)][string] $Description,
        [Parameter(Mandatory)][scriptblock] $Condition,
        [scriptblock] $Describe,
        [ValidateRange(1, 10000)][int] $Attempts = 36,
        [ValidateRange(0, 3600)][int] $IntervalSeconds = 5
    )
    $value = Wait-Until -Condition $Condition -Attempts $Attempts -IntervalSeconds $IntervalSeconds
    if ($null -ne $value) { return $value }
    $seen = $Describe ? " ($(& $Describe))" : ''
    throw "timed out after $($Attempts * $IntervalSeconds) seconds waiting for $Description$seen"
}

function Invoke-SmokeStep {
    <#
    .SYNOPSIS Runs one step of the smoke run. It prints PASS and returns what the step returned, or fails naming the step.
    #>
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][scriptblock] $Action
    )
    try { $result = & $Action }
    catch { Write-Fail "${Name}: $($_.Exception.Message)" }
    Write-Pass $Name
    $result
}

Export-ModuleMember -Function ConvertTo-Base64Url, Get-LocalToken, Get-IngressUrl, Test-Status, Wait-ForValue, Invoke-SmokeStep
