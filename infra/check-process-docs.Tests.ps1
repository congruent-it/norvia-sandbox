#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    $script:script = Join-Path $PSScriptRoot 'check-process-docs.ps1'

    # A throwaway repo layout: one TMS doc (P1) and one ERP doc (E1), both catalogued and in status.md.
    function Get-DocsTree {
        $root = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid())
        New-Item -ItemType Directory -Path "$root/docs/processes/tms", "$root/docs/processes/erp" | Out-Null
        Set-Content "$root/docs/processes/tms/p1-intake.md" '# P1'
        Set-Content "$root/docs/processes/erp/e1-invoicing.md" '# E1'
        Set-Content "$root/docs/processes/tms/README.md" '| P1 | [Intake](p1-intake.md) |'
        Set-Content "$root/docs/processes/erp/README.md" '| E1 | [Invoicing](e1-invoicing.md) |'
        Set-Content "$root/docs/status.md" 'TMS (P1). ERP (E1).'
        $root
    }

    function Invoke-Check($Root) {
        $output = pwsh -NoProfile -File $script -Root $Root 2>&1 | Out-String
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
}

Describe 'check-process-docs' {
    BeforeEach { $script:root = Get-DocsTree }
    AfterEach { Remove-Item $script:root -Recurse -Force }

    It 'passes when every process doc is in its catalogue and in status.md' {
        (Invoke-Check $root).ExitCode | Should -Be 0
    }

    It 'passes on the repo itself' {
        (Invoke-Check (git -C $PSScriptRoot rev-parse --show-toplevel)).ExitCode | Should -Be 0
    }

    It 'fails naming the doc and its catalogue when a doc is not in the catalogue' {
        Set-Content "$root/docs/processes/tms/p2-rating.md" '# P2'
        Set-Content "$root/docs/status.md" 'TMS (P1, P2). ERP (E1).'
        $result = Invoke-Check $root
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'p2-rating\.md'
        $result.Output | Should -Match 'docs/processes/tms/README\.md'
        $result.Output | Should -Not -Match 'docs/status\.md'
    }

    It 'fails naming the doc and docs/status.md when a doc is not in status.md' {
        Set-Content "$root/docs/processes/tms/p2-rating.md" '# P2'
        Add-Content "$root/docs/processes/tms/README.md" '| P2 | [Rating](p2-rating.md) |'
        $result = Invoke-Check $root
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'p2-rating\.md'
        $result.Output | Should -Match 'docs/status\.md'
    }

    It 'names both files when a doc is in neither' {
        Set-Content "$root/docs/processes/tms/p9-settlement.md" '# P9'
        $result = Invoke-Check $root
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'docs/processes/tms/README\.md'
        $result.Output | Should -Match 'docs/status\.md'
    }

    It 'accepts a range of ids with an en dash in status.md' {
        Set-Content "$root/docs/processes/tms/p10-capacity.md" '# P10'
        Add-Content "$root/docs/processes/tms/README.md" '| P10 | [Capacity](p10-capacity.md) |'
        Set-Content "$root/docs/status.md" "TMS (P1$([char]0x2013)P12). ERP (E1)."
        (Invoke-Check $root).ExitCode | Should -Be 0
    }
}
