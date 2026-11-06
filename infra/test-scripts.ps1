#Requires -Version 7.4
<#
.SYNOPSIS
    The gate for the repo's PowerShell (ADR 0048): parses every .ps1 and .psm1, runs PSScriptAnalyzer and the Pester tests.
.DESCRIPTION
    CI runs it; run it locally the same way: pwsh infra/test-scripts.ps1
    Needs the PSScriptAnalyzer and Pester modules (Install-PSResource PSScriptAnalyzer, Pester).
    A syntax error or an analyzer error fails it; analyzer warnings are listed but do not.
#>
$ErrorActionPreference = 'Stop'
$root = (git -C $PSScriptRoot rev-parse --show-toplevel)
$failed = $false

# Files git knows (tracked or new, not ignored): build output (bin/, obj/) holds PowerShell that is not ours, such as Playwright's.
$files = git -C $root ls-files --cached --others --exclude-standard '*.ps1' '*.psm1' | ForEach-Object { Join-Path $root $_ }
Write-Host "Checking $($files.Count) PowerShell files"

foreach ($file in $files) {
    $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($file, [ref]$null, [ref]$errors)
    foreach ($e in $errors) {
        $failed = $true
        Write-Host "::error file=$file,line=$($e.Extent.StartLineNumber)::$($e.Message)"
    }
}

$settings = Join-Path $PSScriptRoot 'PSScriptAnalyzerSettings.psd1'
foreach ($file in $files) {
    foreach ($finding in Invoke-ScriptAnalyzer -Path $file -Settings $settings) {
        $level = $finding.Severity -eq 'Error' ? 'error' : 'warning'
        if ($level -eq 'error') { $failed = $true }
        Write-Host "::$level file=$file,line=$($finding.Line)::$($finding.RuleName): $($finding.Message)"
    }
}

$tests = Invoke-Pester -Path (Join-Path $PSScriptRoot '*') -PassThru -Output Detailed
if ($tests.FailedCount -gt 0) { $failed = $true }

if ($failed) { throw 'The PowerShell checks failed' }
Write-Host 'The PowerShell checks passed'
