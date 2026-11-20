#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    $script:script = Join-Path $PSScriptRoot 'compare-ci-matrices.ps1'

    # A leg is a hashtable of application, directory and kinds; -Raw is the include block as written, for the malformed cases.
    function New-Workflow([string] $Dir, [string] $Name, [hashtable[]] $Legs, [string] $Raw = '') {
        $path = Join-Path $Dir $Name
        $include = $Raw ? $Raw : ($Legs | ForEach-Object {
                "          - application: $($_.application)`n            directory: $($_.directory)`n            kinds: $($_.kinds)" }) -join "`n"
        Set-Content $path "jobs:`n  integration:`n    strategy:`n      matrix:`n        include:$($include ? "`n$include" : ' []')"
        $path
    }

    function Invoke-Compare($Github, $Forgejo) {
        $output = pwsh -NoProfile -File $script -Github $Github -Forgejo $Forgejo 2>&1 | Out-String
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }

    $script:tms = @{ application = 'TMS'; directory = 'Tms'; kinds = 'UnitTests ArchitectureTests' }
    $script:billing = @{ application = 'TMS (Billing)'; directory = 'Tms'; kinds = 'IntegrationTests.Billing' }
}

Describe 'compare-ci-matrices' {
    BeforeEach {
        $script:dir = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid())
        New-Item -ItemType Directory $dir | Out-Null
    }
    AfterEach { Remove-Item $script:dir -Recurse -Force }

    It 'passes when both matrices have the same legs, in any order' {
        $github = New-Workflow $dir 'github.yml' $tms, $billing
        $forgejo = New-Workflow $dir 'forgejo.yml' $billing, $tms
        (Invoke-Compare $github $forgejo).ExitCode | Should -Be 0
    }

    It 'fails naming a leg that only GitHub has' {
        $github = New-Workflow $dir 'github.yml' $tms, $billing
        $forgejo = New-Workflow $dir 'forgejo.yml' $tms
        $result = Invoke-Compare $github $forgejo
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'TMS \(Billing\).*missing from .*Forgejo'
    }

    It 'fails naming a leg that only Forgejo has' {
        $github = New-Workflow $dir 'github.yml' $tms
        $forgejo = New-Workflow $dir 'forgejo.yml' $tms, $billing
        $result = Invoke-Compare $github $forgejo
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'TMS \(Billing\).*missing from .*GitHub'
    }

    It 'fails when a leg has different kinds or directory on the two sides' {
        $github = New-Workflow $dir 'github.yml' $tms
        $forgejo = New-Workflow $dir 'forgejo.yml' @(@{ application = 'TMS'; directory = 'Tms'; kinds = 'UnitTests' })
        $result = Invoke-Compare $github $forgejo
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'TMS.*differs'
    }

    It 'fails when Forgejo has two legs of one name, as they would share a status' {
        $github = New-Workflow $dir 'github.yml' $tms
        $forgejo = New-Workflow $dir 'forgejo.yml' $tms, $tms
        (Invoke-Compare $github $forgejo).ExitCode | Should -Be 1
    }

    It 'fails when GitHub has two legs of one name' {
        $github = New-Workflow $dir 'github.yml' $tms, $tms
        $forgejo = New-Workflow $dir 'forgejo.yml' $tms
        (Invoke-Compare $github $forgejo).ExitCode | Should -Be 1
    }

    It 'fails when a matrix has no legs, instead of passing on two empty matrices' {
        $empty = New-Workflow $dir 'empty.yml' @()
        $result = Invoke-Compare $empty $empty
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'has no matrix.include'
    }

    It 'fails when the job has no matrix at all' {
        $path = Join-Path $dir 'no-matrix.yml'
        Set-Content $path "jobs:`n  integration:`n    runs-on: x"
        $result = Invoke-Compare $path $path
        $result.ExitCode | Should -Not -Be 0
    }

    It 'passes on the repo itself' {
        $root = git -C $PSScriptRoot rev-parse --show-toplevel
        (Invoke-Compare "$root/.github/workflows/ci.yml" "$root/.forgejo/workflows/ci.yml").ExitCode | Should -Be 0
    }
}
