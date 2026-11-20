#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# The aggregate status forgejo/ci (#1079): waits for the statuses of the workflows and rolls them up. Seam: the script run with the GitHub API
# (Invoke-RestMethod) and Start-Sleep mocked.
BeforeAll {
    $script:script = Join-Path $PSScriptRoot 'gate.ps1'
    $script:manifest = Join-Path $TestDrive 'ci-scope.psd1'
    Set-Content $script:manifest "@{ Applications = @{ tms = @('a/*') }; Shared = @(); Ignored = @() }"
    $script:common = @{
        Sha        = '0123456789abcdef0123456789abcdef01234567'
        Repository = 'congruent-it/norvia-sandbox'
        Token      = 'secret-token'
        Context    = @('forgejo/build', 'forgejo/tms')
        PollSeconds = 1
        Manifest   = $script:manifest
        TimeoutMinutes = 0.1
    }
    function script:Statuses([hashtable] $States) {
        [pscustomobject]@{ statuses = @($States.GetEnumerator() | ForEach-Object { [pscustomobject]@{ context = $_.Key; state = $_.Value } }) }
    }
}

Describe 'gate' {
    BeforeEach { Mock Start-Sleep { } }

    It 'succeeds when every required context succeeded' {
        Mock Invoke-RestMethod { Statuses @{ 'forgejo/build' = 'success'; 'forgejo/tms' = 'success'; 'forgejo/ci' = 'pending' } }

        (& $script:script @script:common).State | Should -Be 'success'
    }

    It 'reads the combined status of the commit with the token' {
        Mock Invoke-RestMethod { Statuses @{ 'forgejo/build' = 'success'; 'forgejo/tms' = 'success' } }

        & $script:script @script:common | Out-Null

        Should -Invoke Invoke-RestMethod -ParameterFilter {
            $Uri -like 'https://api.github.com/repos/congruent-it/norvia-sandbox/commits/0123456789abcdef0123456789abcdef01234567/status*' -and
            $Headers.Authorization -eq 'Bearer secret-token'
        }
    }

    It 'fails as soon as one required context failed, without waiting for the others' {
        Mock Invoke-RestMethod { Statuses @{ 'forgejo/build' = 'failure' } }

        $result = & $script:script @script:common

        $result.State | Should -Be 'failure'
        $result.Description | Should -BeLike '*forgejo/build*'
        Should -Invoke Start-Sleep -Exactly 0
    }

    It 'treats error as a failure' {
        Mock Invoke-RestMethod { Statuses @{ 'forgejo/build' = 'success'; 'forgejo/tms' = 'error' } }

        (& $script:script @script:common).State | Should -Be 'failure'
    }

    It 'waits while a required context is pending or not yet reported, then succeeds' {
        $counter = @{ Calls = 0 }
        $first = Statuses @{ 'forgejo/build' = 'pending' }
        $second = Statuses @{ 'forgejo/build' = 'success'; 'forgejo/tms' = 'pending' }
        $third = Statuses @{ 'forgejo/build' = 'success'; 'forgejo/tms' = 'success' }
        Mock Invoke-RestMethod {
            $counter.Calls++
            switch ($counter.Calls) { 1 { $first } 2 { $second } default { $third } }
        }.GetNewClosure()

        (& $script:script @script:common).State | Should -Be 'success'
        Should -Invoke Start-Sleep -Exactly 2
    }

    It 'errors when a required context never reports, naming it' {
        Mock Invoke-RestMethod { Statuses @{ 'forgejo/build' = 'success' } }
        $arguments = $script:common.Clone(); $arguments.TimeoutMinutes = 0
        $result = & $script:script @arguments

        $result.State | Should -Be 'error'
        $result.Description | Should -BeLike '*forgejo/tms*'
    }

    It 'takes the contexts from the applications of the manifest and adds the repo-level ones' {
        $manifest = Join-Path $TestDrive 'ci-scope.psd1'
        Set-Content $manifest "@{ Applications = @{ alpha = @('a/*'); beta = @('b/*') }; Shared = @(); Ignored = @() }"
        Mock Invoke-RestMethod { Statuses @{ 'forgejo/build' = 'success'; 'forgejo/alpha' = 'success'; 'forgejo/beta' = 'failure' } }
        $arguments = $script:common.Clone(); $arguments.Remove('Context'); $arguments.Manifest = $manifest

        $result = & $script:script @arguments -Context 'forgejo/build'

        $result.State | Should -Be 'failure'
        $result.Description | Should -BeLike '*forgejo/beta*'
    }
}
