#Requires -Version 7.4
<#
.SYNOPSIS
    Posts a commit status to GitHub for a Forgejo CI job (#1039, ADR 0055).
.DESCRIPTION
    Called by the report-status action. Context is forgejo/<Job>. A failed call throws, so the job fails:
    a gate that cannot report is not green. 'cancelled' is reported as an error, so a superseded run's
    pending status does not stay forever.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('pending', 'success', 'failure', 'error', 'cancelled')][string] $State,
    [Parameter(Mandatory)][string] $Job,
    [Parameter(Mandatory)][string] $Sha,
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][string] $TargetUrl,
    [Parameter(Mandatory)][string] $Token,
    [string] $Description
)
$ErrorActionPreference = 'Stop'

if ($State -eq 'cancelled') {
    $State = 'error'
    if (-not $Description) { $Description = 'Cancelled: superseded by a newer push' }
}
if (-not $Description) {
    $Description = switch ($State) {
        'pending' { 'Running' }
        'success' { 'Passed' }
        'failure' { 'Failed' }
        'error'   { 'Errored' }
    }
}
if ($Description.Length -gt 140) { $Description = $Description.Substring(0, 140) }

$body = @{
    state       = $State
    context     = "forgejo/$Job"
    description = $Description
    target_url  = $TargetUrl
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
    -Uri "https://api.github.com/repos/$Repository/statuses/$Sha" `
    -Headers @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' } `
    -ContentType 'application/json' `
    -Body $body | Out-Null
