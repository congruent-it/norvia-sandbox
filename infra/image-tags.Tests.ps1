#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    $script:script = Join-Path $PSScriptRoot 'image-tags.ps1'
    $script:sha = '0123456789abcdef0123456789abcdef01234567'

    function Invoke-Tags([string] $Ref) {
        $output = pwsh -NoProfile -File $script -Ref $Ref -Sha $sha 2>&1 | Out-String
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Tags = @($output -split '\r?\n' | Where-Object { $_ }) }
    }
}

Describe 'image-tags' {
    It 'gives main the short SHA and the floating latest' {
        $result = Invoke-Tags 'refs/heads/main'
        $result.ExitCode | Should -Be 0
        $result.Tags | Should -Be '0123456', 'latest'
    }

    It 'gives a version tag the short SHA and the version, and never latest' {
        $result = Invoke-Tags 'refs/tags/v1.2.3'
        $result.ExitCode | Should -Be 0
        $result.Tags | Should -Be '0123456', 'v1.2.3'
    }

    It 'gives an image/ branch only the short SHA' {
        $result = Invoke-Tags 'refs/heads/image/tms-api-fix'
        $result.ExitCode | Should -Be 0
        $result.Tags | Should -Be '0123456'
    }

    It 'gives any other branch no tag, so nothing is pushed' -ForEach 'refs/heads/1044-feature', 'refs/heads/imagery', 'refs/tags/release-1' {
        $result = Invoke-Tags $_
        $result.ExitCode | Should -Be 0
        $result.Tags | Should -BeNullOrEmpty
    }

    It 'fails on a SHA that is not a commit id' {
        pwsh -NoProfile -File $script -Ref 'refs/heads/main' -Sha 'not-a-sha' 2>&1 | Out-Null
        $LASTEXITCODE | Should -Not -Be 0
    }
}
