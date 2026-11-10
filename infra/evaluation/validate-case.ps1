#Requires -Version 7.4
<#
.SYNOPSIS
    Problems with a case file of the agent evaluation harness (#501), as a compact JSON array of strings; empty when the case is well formed.
.DESCRIPTION
    Takes the case's JSON on the pipeline or on standard input.
    Run it by hand: Get-Content evaluation/cases/<agent>/<case>.json | pwsh infra/evaluation/validate-case.ps1
#>
[CmdletBinding()]
param([Parameter(ValueFromPipeline)][string] $Json)
begin { $lines = [Collections.Generic.List[string]]::new() }
process { if (-not [string]::IsNullOrWhiteSpace($Json)) { $lines.Add($Json) } }
end {
    $ErrorActionPreference = 'Stop'
    # A case has no fixed set of properties: a missing one is $null.
    Set-StrictMode -Off

    # Standard input of `pwsh -File` is not the pipeline.
    if ($lines.Count -eq 0) { $lines.Add([Console]::In.ReadToEnd()) }
    $case = ($lines -join "`n") | ConvertFrom-Json -Depth 50
    $problems = [Collections.Generic.List[string]]::new()

    function Test-StringList($Value) {
        $Value -is [array] -and @($Value | Where-Object { $_ -isnot [string] }).Count -eq 0
    }
    function Test-Object($Value) { $Value -is [pscustomobject] }
    function Test-Number($Value) { $Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal] }
    function Get-PropertyName($Value) { @($Value.PSObject.Properties.Name) }

    if ($case.id -isnot [string] -or $case.id -cnotmatch '^[a-z0-9][a-z0-9-]*$') { $problems.Add('id must be lower-case letters, digits and hyphens') }
    if ($case.agent -isnot [string] -or $case.agent.Length -eq 0) { $problems.Add('agent must name the agent') }
    if ($case.input -isnot [string] -or $case.input.Length -eq 0) { $problems.Add('input must be the question or text put to the agent') }

    $expect = $case.expect
    if (-not (Test-Object $expect)) { $problems.Add('expect must be an object of checks') }
    else {
        $checks = 'fields', 'lists', 'mustNotContain', 'mustCite', 'toolsNotCalled', 'toolsCalled', 'explanation', 'texts', 'absent', 'grounded'
        foreach ($name in (Get-PropertyName $expect | Sort-Object -CaseSensitive)) {
            if ($name -cnotin $checks) { $problems.Add("expect.$name is not a check") }
        }

        if ($null -ne $expect.fields -and -not (Test-Object $expect.fields)) { $problems.Add('expect.fields must be an object of path: value') }

        if ($null -ne $expect.lists) {
            $malformed = -not (Test-Object $expect.lists)
            if (-not $malformed) {
                foreach ($entry in $expect.lists.PSObject.Properties) {
                    $rule = $entry.Value
                    if (-not (Test-Object $rule)) { $malformed = $true; continue }
                    $names = Get-PropertyName $rule
                    if ($names.Count -eq 0 -or @($names | Where-Object { $_ -cnotin 'exactly', 'includes', 'excludes' }).Count -gt 0) { $malformed = $true; continue }
                    foreach ($list in $rule.PSObject.Properties) { if (-not (Test-StringList $list.Value)) { $malformed = $true } }
                }
            }
            if ($malformed) { $problems.Add('expect.lists must be an object of path: {exactly, includes or excludes: list of strings}') }
        }

        foreach ($key in 'mustNotContain', 'mustCite', 'toolsNotCalled', 'toolsCalled', 'absent') {
            if ($null -ne $expect.$key -and -not (Test-StringList $expect.$key)) { $problems.Add("expect.$key must be a list of strings") }
        }

        if ($null -ne $expect.grounded -and $expect.grounded -isnot [bool]) { $problems.Add('expect.grounded must be true or false') }

        if ($null -ne $expect.explanation -and (-not (Test-Object $expect.explanation) -or $expect.explanation.rubric -isnot [string])) {
            $problems.Add('expect.explanation must be an object with a rubric')
        }

        $texts = $expect.texts
        if ($null -ne $texts) {
            $malformed = -not (Test-Object $texts)
            if (-not $malformed) {
                $names = Get-PropertyName $texts
                $malformed = $names.Count -eq 0 -or
                @($names | Where-Object { $_ -cnotin 'minCount', 'maxLength', 'safe', 'maxDuplicateShare', 'instructionLike' }).Count -gt 0 -or
                ($null -ne $texts.minCount -and -not (Test-Number $texts.minCount)) -or
                ($null -ne $texts.maxLength -and -not (Test-Number $texts.maxLength)) -or
                ($null -ne $texts.maxDuplicateShare -and -not (Test-Number $texts.maxDuplicateShare)) -or
                ($null -ne $texts.safe -and $texts.safe -isnot [bool]) -or
                ($null -ne $texts.instructionLike -and $texts.instructionLike -cnotin 'none', 'most')
            }
            if ($malformed) {
                $problems.Add('expect.texts must be an object of minCount, maxLength, safe, maxDuplicateShare or instructionLike ("none" or "most")')
            }
        }

        if ((Get-PropertyName $expect).Count -eq 0) { $problems.Add('expect must hold at least one check') }
    }

    # The TMS's known answer for the draft (the Intake agent's cases): the booking path accepts it, or rejects it with these codes.
    $submission = $case.submission
    if ($null -ne $submission) {
        $codes = $submission.codes
        $valid = (Test-Object $submission) -and $submission.result -cin 'ACCEPTED', 'REJECTED' -and
        (($submission.result -ceq 'ACCEPTED' -and $null -eq $codes) -or
        ($submission.result -ceq 'REJECTED' -and (Test-StringList $codes) -and @($codes).Count -gt 0))
        if (-not $valid) { $problems.Add('submission must be {"result": "ACCEPTED"} or {"result": "REJECTED", "codes": [the TMS rejection codes]}') }
    }

    if ($null -ne $case.setup -and -not (Test-Object $case.setup)) { $problems.Add('setup must be an object (toggle, fault, seed)') }

    ConvertTo-Json -InputObject @($problems) -Compress
}
