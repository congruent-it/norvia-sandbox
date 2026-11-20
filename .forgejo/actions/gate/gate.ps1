#Requires -Version 7.4
<#
.SYNOPSIS
    The aggregate status of a push, forgejo/ci (#1079).
.DESCRIPTION
    Called at the end of every workflow of the push (the gate action). Every application workflow reports forgejo/<application>, also when the change does
    not affect it (then Passed, "Not affected"), and the repo-level jobs report forgejo/build and forgejo/powershell. This reads the combined status of the
    commit once and decides: success when all of them succeeded, failure as soon as one failed or errored, and nothing while one is still pending or has
    not reported. The job that finishes last therefore reports success, and the first to see a failure reports it.
    It never waits: a job that waits holds a runner slot, and the workflows it waits for need that slot (the runner runs `capacity` jobs at once).
    Branch protection requires forgejo/ci alone, so a new workflow needs no change to it. The contexts are -Context plus one per application of the manifest.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Sha,
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][string] $Token,
    [Parameter(Mandatory)][string] $TargetUrl,
    [string[]] $Context = @('forgejo/powershell', 'forgejo/build'),
    [string] $Manifest = (Join-Path $PSScriptRoot '../../ci-scope.psd1')
)
$ErrorActionPreference = 'Stop'

$required = @($Context) + @((Import-PowerShellDataFile $Manifest).Applications.Keys | Sort-Object | ForEach-Object { "forgejo/$_" }) | Select-Object -Unique

# The combined status lists the latest status of each context.
$response = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Sha/status?per_page=100" `
    -Headers @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$state = @{}
foreach ($status in $response.statuses) { $state[$status.context] = $status.state }

$failed = @($required | Where-Object { $state[$_] -in 'failure', 'error' })
$waiting = @($required | Where-Object { $state[$_] -ne 'success' })

if ($failed) {
    $result = [pscustomobject]@{ State = 'failure'; Description = "Failed: $($failed -join ', ')" }
} elseif ($waiting) {
    return [pscustomobject]@{ State = 'pending'; Description = "Waiting for: $($waiting -join ', ')" }
} else {
    $result = [pscustomobject]@{ State = 'success'; Description = "All $($required.Count) workflows passed" }
}

& (Join-Path $PSScriptRoot '../report-status/report-status.ps1') -State $result.State -Job ci -Sha $Sha -Repository $Repository `
    -TargetUrl $TargetUrl -Token $Token -Description $result.Description
return $result
