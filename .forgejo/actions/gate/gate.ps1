#Requires -Version 7.4
<#
.SYNOPSIS
    The aggregate status of a push, forgejo/ci (#1079).
.DESCRIPTION
    Called by the gate job of ci.yml. Every application workflow reports forgejo/<application>, also when the change does not affect it (then Passed,
    "Not affected"), and the repo-level jobs report forgejo/build and forgejo/powershell. This waits until each of them has reported, and returns the state
    to report as forgejo/ci: success when all succeeded, failure as soon as one failed or errored, error when one never reported within the timeout.
    Branch protection requires forgejo/ci alone, so a new workflow needs no change to it. The contexts are -Context plus one per application of the manifest.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Sha,
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][string] $Token,
    [string[]] $Context = @('forgejo/powershell', 'forgejo/build'),
    [string] $Manifest = (Join-Path $PSScriptRoot '../../ci-scope.psd1'),
    [int] $PollSeconds = 30,
    [double] $TimeoutMinutes = 90
)
$ErrorActionPreference = 'Stop'

$required = @($Context) + @((Import-PowerShellDataFile $Manifest).Applications.Keys | Sort-Object | ForEach-Object { "forgejo/$_" }) | Select-Object -Unique
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)

while ($true) {
    # The combined status lists the latest status of each context.
    $response = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Sha/status?per_page=100" `
        -Headers @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
    $state = @{}
    foreach ($status in $response.statuses) { $state[$status.context] = $status.state }

    $failed = @($required | Where-Object { $state[$_] -in 'failure', 'error' })
    if ($failed) {
        return [pscustomobject]@{ State = 'failure'; Description = "Failed: $($failed -join ', ')" }
    }

    $waiting = @($required | Where-Object { $state[$_] -ne 'success' })
    if (-not $waiting) {
        return [pscustomobject]@{ State = 'success'; Description = "All $($required.Count) workflows passed" }
    }

    if ((Get-Date) -ge $deadline) {
        return [pscustomobject]@{ State = 'error'; Description = "No result within $TimeoutMinutes minutes: $($waiting -join ', ')" }
    }
    Start-Sleep -Seconds $PollSeconds
}
