#Requires -Version 7.4
<#
.SYNOPSIS
    Fails when the test matrix of the Forgejo workflow and the one of the GitHub workflow are not the same legs.
.DESCRIPTION
    CI runs it: pwsh infra/compare-ci-matrices.ps1
    Both CI systems run the tests while the move to Forgejo is under way (#1030). A leg added to one matrix and not the other would be tested on one
    system only, so this fails on a leg that is missing from either side, or whose directory or kinds differ. Legs are matched by their application,
    which is also the name of the leg's status on Forgejo, so it must be unique there. The order of the legs does not matter.
    Needs yq (infra/tool-versions.psd1), which reads the workflows.
#>
param(
    [string] $Github = (Join-Path (git -C $PSScriptRoot rev-parse --show-toplevel) '.github/workflows/ci.yml'),
    [string] $Forgejo = (Join-Path (git -C $PSScriptRoot rev-parse --show-toplevel) '.forgejo/workflows/ci.yml'),
    [string] $Job = 'integration'
)
$ErrorActionPreference = 'Stop'

function Get-Legs([string] $Path) {
    $json = yq -o=json ".jobs.$Job.strategy.matrix.include" $Path
    if ($LASTEXITCODE -ne 0) { throw "yq could not read the matrix of job '$Job' in $Path" }
    $legs = @($json | ConvertFrom-Json)
    if (-not $legs) { throw "Job '$Job' in $Path has no matrix.include" }
    $legs
}

$githubLegs = Get-Legs $Github
$forgejoLegs = Get-Legs $Forgejo
$failures = [Collections.Generic.List[string]]::new()

foreach ($side in @(@{ Name = 'GitHub'; Path = $Github; Legs = $githubLegs }, @{ Name = 'Forgejo'; Path = $Forgejo; Legs = $forgejoLegs })) {
    foreach ($duplicate in $side.Legs | Group-Object application | Where-Object Count -gt 1) {
        $failures.Add("$($side.Name) has $($duplicate.Count) legs named '$($duplicate.Name)' ($($side.Path)); they would share one status")
    }
}

$githubByName = $githubLegs | Group-Object application -AsHashTable -AsString
$forgejoByName = $forgejoLegs | Group-Object application -AsHashTable -AsString

foreach ($name in $githubByName.Keys | Sort-Object) {
    if (-not $forgejoByName.ContainsKey($name)) {
        $failures.Add("Leg '$name' is missing from the Forgejo matrix ($Forgejo)")
        continue
    }
    $onGithub = $githubByName[$name][0]
    $onForgejo = $forgejoByName[$name][0]
    if ($onGithub.directory -ne $onForgejo.directory -or $onGithub.kinds -ne $onForgejo.kinds) {
        $failures.Add("Leg '$name' differs: GitHub has directory '$($onGithub.directory)', kinds '$($onGithub.kinds)'; Forgejo has directory '$($onForgejo.directory)', kinds '$($onForgejo.kinds)'")
    }
}
foreach ($name in $forgejoByName.Keys | Sort-Object) {
    if (-not $githubByName.ContainsKey($name)) {
        $failures.Add("Leg '$name' is missing from the GitHub matrix ($Github)")
    }
}

foreach ($failure in $failures) { Write-Host "::error::$failure" }
if ($failures) { exit 1 }
Write-Host "The two matrices have the same $($githubLegs.Count) legs"
