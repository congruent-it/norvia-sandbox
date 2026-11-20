#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# The script of the report-status action (#1039). Seam: the script run with its parameters, Invoke-RestMethod mocked.
BeforeAll {
    $script:script = Join-Path $PSScriptRoot 'report-status.ps1'
    $script:common = @{
        Job        = 'build'
        Sha        = '0123456789abcdef0123456789abcdef01234567'
        Repository = 'congruent-it/norvia-sandbox'
        TargetUrl  = 'https://git.home.arpa/owner/norvia-sandbox/actions/runs/7'
        Token      = 'secret-token'
    }
}

Describe 'report-status' {
    BeforeEach { Mock Invoke-RestMethod { } }

    It 'posts <State> to the commit status of the GitHub repository' -ForEach @(
        @{ State = 'pending' }, @{ State = 'success' }, @{ State = 'failure' }, @{ State = 'error' }
    ) {
        & $script:script @script:common -State $State

        Should -Invoke Invoke-RestMethod -Exactly 1 -ParameterFilter {
            $Method -eq 'Post' -and
            $Uri -eq 'https://api.github.com/repos/congruent-it/norvia-sandbox/statuses/0123456789abcdef0123456789abcdef01234567' -and
            ($Body | ConvertFrom-Json).state -eq $State
        }
    }

    It 'names the context forgejo/<job> and points the target_url at the Forgejo run' {
        & $script:script @script:common -State success

        Should -Invoke Invoke-RestMethod -Exactly 1 -ParameterFilter {
            $b = $Body | ConvertFrom-Json
            $b.context -eq 'forgejo/build' -and $b.target_url -eq 'https://git.home.arpa/owner/norvia-sandbox/actions/runs/7'
        }
    }

    It 'authenticates with the token as a bearer token' {
        & $script:script @script:common -State success

        Should -Invoke Invoke-RestMethod -Exactly 1 -ParameterFilter {
            $Headers['Authorization'] -eq 'Bearer secret-token'
        }
    }

    It 'reports a cancelled job as an error that says it was superseded' {
        & $script:script @script:common -State cancelled

        Should -Invoke Invoke-RestMethod -Exactly 1 -ParameterFilter {
            $b = $Body | ConvertFrom-Json
            $b.state -eq 'error' -and $b.description -match 'superseded'
        }
    }

    It 'keeps the description within the 140 characters GitHub accepts' {
        & $script:script @script:common -State failure -Description ('x' * 300)

        Should -Invoke Invoke-RestMethod -Exactly 1 -ParameterFilter {
            ($Body | ConvertFrom-Json).description.Length -le 140
        }
    }

    It 'rejects a state GitHub does not know' {
        { & $script:script @script:common -State skipped } | Should -Throw
        Should -Invoke Invoke-RestMethod -Exactly 0
    }

    It 'fails when the call fails, so a gate that cannot report is not green' {
        Mock Invoke-RestMethod { throw 'HTTP 403' }

        { & $script:script @script:common -State success } | Should -Throw '*403*'
    }
}
