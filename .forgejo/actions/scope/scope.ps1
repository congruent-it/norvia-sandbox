#Requires -Version 7.4
<#
.SYNOPSIS
    Says whether a change affects an application's CI workflow (#1079).
.DESCRIPTION
    Called by the scope action, first in every application workflow. Returns $true when the workflow must run its tests.
    The change is the files of -ChangedFile, or else the diff of HEAD against its merge-base with -Base. The rules, over .forgejo/ci-scope.psd1:
      1. A file matching the application's own paths, or a Shared entry that lists the application, affects it.
      2. A file that matches no application's paths and no Shared entry is ignored only when it matches Ignored.
      3. Any other file affects every application, so a folder the manifest does not know is never skipped.
    Everything is affected under -RunAll (main), when no file is known, or when the base cannot be resolved. Paths are prefix globs (* crosses folders).
#>
[CmdletBinding()]
[OutputType([bool])]
param(
    [Parameter(Mandatory)][string] $Application,
    [string[]] $ChangedFile,
    [string] $Base = 'origin/main',
    [string] $Repository = '.',
    [string] $Manifest = (Join-Path $PSScriptRoot '../../ci-scope.psd1'),
    [switch] $RunAll
)
$ErrorActionPreference = 'Stop'

$scope = Import-PowerShellDataFile $Manifest
if (-not $scope.Applications.ContainsKey($Application)) {
    throw "The application '$Application' is not in $Manifest. Add it, with its workflow, or fix the name."
}

if ($RunAll) { return $true }

if (-not $PSBoundParameters.ContainsKey('ChangedFile')) {
    $mergeBase = git -C $Repository merge-base $Base HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $mergeBase) {
        Write-Host "No merge-base with ${Base}: running everything."
        return $true
    }
    $ChangedFile = @(git -C $Repository diff --name-only $mergeBase HEAD)
}
if (-not $ChangedFile -or $ChangedFile.Count -eq 0) { return $true }

function Test-Match([string] $File, [string[]] $Patterns) {
    foreach ($pattern in $Patterns) { if ($File -like $pattern) { return $true } }
    return $false
}

foreach ($file in $ChangedFile) {
    $claimed = $false
    foreach ($name in $scope.Applications.Keys) {
        if (Test-Match $file $scope.Applications[$name]) {
            $claimed = $true
            if ($name -eq $Application) { return $true }
        }
    }
    foreach ($entry in $scope.Shared) {
        if (Test-Match $file $entry.Paths) {
            $claimed = $true
            if ($entry.Applications -contains $Application) { return $true }
        }
    }
    if (-not $claimed -and -not (Test-Match $file $scope.Ignored)) { return $true }
}
return $false
