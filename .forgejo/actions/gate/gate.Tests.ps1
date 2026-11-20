#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# The aggregate status forgejo/ci (#1079): the last job of the push to finish rolls the statuses of the workflows up. It never waits, because a job that
# waits holds a runner slot the workflows it waits for need. Seam: the script run with the GitHub API (Invoke-RestMethod) mocked.
BeforeAll {
    $script:script = Join-Path $PSScriptRoot 'gate.ps1'
    $script:manifest = Join-Path $TestDrive 'ci-scope.psd1'
    Set-Content $script:manifest "@{ Applications = @{ tms = @('a/*') }; Shared = @(); Ignored = @() }"
    $script:common = @{
        Sha        = '0123456789abcdef0123456789abcdef01234567'
        Repository = 'congruent-it/norvia-sandbox'
        Token      = 'secret-token'
        TargetUrl  = 'https://git.home.arpa/owner/norvia-sandbox/actions/runs/7'
        Context    = @('forgejo/build', 'forgejo/tms')
        Manifest   = $script:manifest
    }
    # The GET returns these statuses; the POST of the result returns nothing.
    function script:Respond([hashtable] $States) {
        $response = [pscustomobject]@{ statuses = @($States.GetEnumerator() | ForEach-Object { [pscustomobject]@{ context = $_.Key; state = $_.Value } }) }
        Mock Invoke-RestMethod { $response }.GetNewClosure() -ParameterFilter { $Method -ne 'Post' }
        Mock Invoke-RestMethod { } -ParameterFilter { $Method -eq 'Post' }
    }
    function script:Posted { param($State) Should -Invoke Invoke-RestMethod -Exactly 1 -ParameterFilter { $Method -eq 'Post' -and ($Body | ConvertFrom-Json).state -eq $State -and ($Body | ConvertFrom-Json).context -eq 'forgejo/ci' } }
}

Describe 'gate' {
    It 'reports success when every required context succeeded' {
        Respond @{ 'forgejo/build' = 'success'; 'forgejo/tms' = 'success'; 'forgejo/ci' = 'pending' }

        (& $script:script @script:common).State | Should -Be 'success'
        Posted 'success'
    }

    It 'reads the combined status of the commit with the token' {
        Respond @{ 'forgejo/build' = 'success'; 'forgejo/tms' = 'success' }

        & $script:script @script:common | Out-Null

        Should -Invoke Invoke-RestMethod -ParameterFilter {
            $Method -ne 'Post' -and
            $Uri -like 'https://api.github.com/repos/congruent-it/norvia-sandbox/commits/0123456789abcdef0123456789abcdef01234567/status*' -and
            $Headers.Authorization -eq 'Bearer secret-token'
        }
    }

    It 'reports failure as soon as one required context failed, without the others' {
        Respond @{ 'forgejo/build' = 'failure' }

        $result = & $script:script @script:common

        $result.State | Should -Be 'failure'
        $result.Description | Should -BeLike '*forgejo/build*'
        Posted 'failure'
    }

    It 'treats error as a failure' {
        Respond @{ 'forgejo/build' = 'success'; 'forgejo/tms' = 'error' }

        (& $script:script @script:common).State | Should -Be 'failure'
        Posted 'failure'
    }

    It 'reports nothing while a required context is pending or has not reported, leaving it to the job that finishes last' {
        Respond @{ 'forgejo/build' = 'success'; 'forgejo/tms' = 'pending' }

        $result = & $script:script @script:common

        $result.State | Should -Be 'pending'
        $result.Description | Should -BeLike '*forgejo/tms*'
        Should -Invoke Invoke-RestMethod -Exactly 0 -ParameterFilter { $Method -eq 'Post' }
    }

    It 'takes the contexts from the applications of the manifest and adds the repo-level ones' {
        $manifest = Join-Path $TestDrive 'two.psd1'
        Set-Content $manifest "@{ Applications = @{ alpha = @('a/*'); beta = @('b/*') }; Shared = @(); Ignored = @() }"
        Respond @{ 'forgejo/build' = 'success'; 'forgejo/alpha' = 'success'; 'forgejo/beta' = 'failure' }
        $arguments = $script:common.Clone(); $arguments.Remove('Context'); $arguments.Manifest = $manifest

        $result = & $script:script @arguments -Context 'forgejo/build'

        $result.State | Should -Be 'failure'
        $result.Description | Should -BeLike '*forgejo/beta*'
    }
}
