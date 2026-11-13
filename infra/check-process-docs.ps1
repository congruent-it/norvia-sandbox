#Requires -Version 7.4
<#
.SYNOPSIS
    Fails when a process doc under docs/processes/*/ is missing from its catalogue or from docs/status.md.
.DESCRIPTION
    CI runs it: pwsh infra/check-process-docs.ps1
    A process doc is docs/processes/<area>/<id>-<name>.md (not the catalogue README.md). It is in its catalogue when
    the area's README.md links to it, and in docs/status.md when the page names its id (P10) or a range that covers
    it (P1 to P12 with an en dash).
#>
param(
    [string] $Root = (git -C $PSScriptRoot rev-parse --show-toplevel)
)
$ErrorActionPreference = 'Stop'

$statusPath = 'docs/status.md'
$status = Get-Content (Join-Path $Root $statusPath) -Raw

# Ids status.md names, ranges such as P1, an en dash, P12 expanded.
$named = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($m in [regex]::Matches($status, '\b([A-Z])(\d+)\b(?:\s*[\u2013-]\s*\1?(\d+)\b)?')) {
    $last = $m.Groups[3].Success ? [int] $m.Groups[3].Value : [int] $m.Groups[2].Value
    foreach ($n in [int] $m.Groups[2].Value..$last) { $null = $named.Add("$($m.Groups[1].Value)$n") }
}

$failures = foreach ($doc in Get-ChildItem (Join-Path $Root 'docs/processes/*/*.md') | Where-Object Name -ne 'README.md' | Sort-Object FullName) {
    $catalogue = "docs/processes/$($doc.Directory.Name)/README.md"
    $catalogueText = Get-Content (Join-Path $Root $catalogue) -Raw
    $docPath = "docs/processes/$($doc.Directory.Name)/$($doc.Name)"

    if ($catalogueText -notmatch [regex]::Escape("]($($doc.Name))")) {
        "$docPath is missing from its catalogue, $catalogue"
    }
    if ($doc.Name -notmatch '^([a-z]\d+)-' -or -not $named.Contains($Matches[1])) {
        "$docPath is missing from $statusPath"
    }
}

foreach ($failure in $failures) { Write-Host "::error::$failure" }
if ($failures) { exit 1 }
Write-Host 'Every process doc is in its catalogue and in docs/status.md'
