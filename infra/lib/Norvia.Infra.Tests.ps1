#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'Norvia.Infra.psm1') -Force
}

Describe 'Write-Pass and Write-Fail' {
    It 'Write-Pass prints PASS and the message' {
        Write-Pass 'it works' 6>&1 | Should -Be 'PASS  it works'
    }

    It 'Write-Fail ends the script with FAIL and the message' {
        { Write-Fail 'it broke' } | Should -Throw 'FAIL  it broke'
    }

    It 'Write-Fail lets a finally block run' {
        $script:cleaned = $false
        try { try { Write-Fail 'x' } finally { $script:cleaned = $true } } catch { $null = $_ }
        $script:cleaned | Should -BeTrue
    }

    It 'exits 1 when an uncaught Write-Fail ends a script' {
        $module = Join-Path $PSScriptRoot 'Norvia.Infra.psm1'
        $null = pwsh -NoProfile -Command "Import-Module '$module'; `$ErrorActionPreference = 'Stop'; Write-Fail 'boom'" 2>&1
        $LASTEXITCODE | Should -Be 1
    }
}

Describe 'Invoke-Az' {
    It 'fails with the arguments and exit code when az fails' {
        Mock -ModuleName Norvia.Infra az { $global:LASTEXITCODE = 3; 'ERROR: nope' }
        { Invoke-Az group show --name x } | Should -Throw '*az group show --name x failed with exit code 3*nope*'
    }

    It 'returns $null for a tolerated failure' {
        Mock -ModuleName Norvia.Infra az { $global:LASTEXITCODE = 1; 'ERROR' }
        Invoke-Az group delete -AllowFailure | Should -BeNullOrEmpty
    }

    It 'returns the output when az succeeds' {
        Mock -ModuleName Norvia.Infra az { $global:LASTEXITCODE = 0; 'value' }
        Invoke-Az group show | Should -Be 'value'
    }
}

Describe 'Invoke-Az stderr' {
    It 'leaves what az writes to stderr out of the output' {
        Mock -ModuleName Norvia.Infra az { $global:LASTEXITCODE = 0; Write-Error 'WARNING: a bicep upgrade' -ErrorAction Continue; '{"a":1}' }
        (Invoke-Az deployment sub create | Out-String | ConvertFrom-Json).a | Should -Be 1
    }
}

Describe 'Get-AppFqdn' {
    It 'trims the line az prints' {
        Mock -ModuleName Norvia.Infra Invoke-Az { "tms-api.example.westeurope.azurecontainerapps.io`n" }
        Get-AppFqdn tms-api -ResourceGroup rg-test | Should -Be 'tms-api.example.westeurope.azurecontainerapps.io'
    }

    It 'asks for the named app in the resource group' {
        Mock -ModuleName Norvia.Infra Invoke-Az { 'a.b' }
        $null = Get-AppFqdn tms-api -ResourceGroup rg-test
        Should -Invoke -ModuleName Norvia.Infra Invoke-Az -ParameterFilter {
            $Arguments -contains 'tms-api' -and $Arguments -contains 'rg-test'
        }
    }

    It 'fails when the app has no ingress' {
        Mock -ModuleName Norvia.Infra Invoke-Az { '' }
        { Get-AppFqdn worker -ResourceGroup rg-test } | Should -Throw 'FAIL  the Container App worker has no ingress FQDN'
    }

    It 'Get-AppUrl adds the scheme' {
        Mock -ModuleName Norvia.Infra Invoke-Az { 'a.b' }
        Get-AppUrl tms-api -ResourceGroup rg-test | Should -Be 'https://a.b'
    }
}

Describe 'Get-ResourceGroupName' {
    BeforeEach {
        $script:saved = @($env:AZURE_RESOURCE_GROUP, $env:AZURE_ENV_NAME)
    }
    AfterEach {
        $env:AZURE_RESOURCE_GROUP, $env:AZURE_ENV_NAME = $script:saved
    }

    It 'prefers AZURE_RESOURCE_GROUP' {
        $env:AZURE_RESOURCE_GROUP = 'rg-custom'; $env:AZURE_ENV_NAME = 'dev'
        Get-ResourceGroupName | Should -Be 'rg-custom'
    }

    It 'falls back to rg- plus the environment name' {
        $env:AZURE_RESOURCE_GROUP = $null; $env:AZURE_ENV_NAME = 'dev'
        Get-ResourceGroupName | Should -Be 'rg-dev'
    }

    It 'fails with neither' {
        $env:AZURE_RESOURCE_GROUP = $null; $env:AZURE_ENV_NAME = $null
        { Get-ResourceGroupName } | Should -Throw 'FAIL*'
    }
}

Describe 'Import-AzdEnvironment' {
    AfterEach {
        Remove-Item Env:NORVIA_TEST_A, Env:NORVIA_TEST_B, Env:NORVIA_TEST_C -ErrorAction SilentlyContinue
    }

    It 'sets quoted and bare values and ignores other lines' {
        Import-AzdEnvironment -Lines @('NORVIA_TEST_A="one two"', 'NORVIA_TEST_B=plain', '# comment', '', 'NORVIA_TEST_C="say \"hi\""')
        $env:NORVIA_TEST_A | Should -Be 'one two'
        $env:NORVIA_TEST_B | Should -Be 'plain'
        $env:NORVIA_TEST_C | Should -Be 'say "hi"'
    }
}

Describe 'Wait-Until' {
    BeforeEach {
        Mock -ModuleName Norvia.Infra Start-Sleep {}
    }

    It 'returns the first result without sleeping' {
        Wait-Until { 'found' } | Should -Be 'found'
        Should -Invoke -ModuleName Norvia.Infra Start-Sleep -Times 0 -Exactly
    }

    It 'asks again until the condition returns something' {
        $script:calls = 0
        $result = Wait-Until { $script:calls++; if ($script:calls -ge 3) { 'late' } } -Attempts 5 -IntervalSeconds 7
        $result | Should -Be 'late'
        $script:calls | Should -Be 3
        Should -Invoke -ModuleName Norvia.Infra Start-Sleep -Times 2 -Exactly -ParameterFilter { $Seconds -eq 7 }
    }

    It 'returns $null after the last attempt, and does not sleep after it' {
        $script:calls = 0
        Wait-Until { $script:calls++; '' } -Attempts 4 | Should -BeNullOrEmpty
        $script:calls | Should -Be 4
        Should -Invoke -ModuleName Norvia.Infra Start-Sleep -Times 3 -Exactly
    }

    It 'treats an empty array and a blank string as not yet' {
        $script:calls = 0
        $result = Wait-Until { $script:calls++; switch ($script:calls) { 1 { , @() } 2 { '  ' } default { 'x' } } } -Attempts 5
        $result | Should -Be 'x'
    }

    It 'accepts 0 and $false-like values that are not empty' {
        Wait-Until { 0 } | Should -Be 0
    }
}

Describe 'Invoke-LogAnalyticsQuery' {
    It 'posts the query in a body file and returns the rows' {
        Mock -ModuleName Norvia.Infra Invoke-Az {
            $path = ($Arguments | Where-Object { $_ -like '@*' }).Substring(1)
            $script:sent = Get-Content -Raw $path | ConvertFrom-Json
            '{"tables":[{"rows":[["a",1],["b",2]]}]}'
        }
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId ws-1 -Query "T | where X == 'y'"
        $rows.Count | Should -Be 2
        $rows[1][0] | Should -Be 'b'
        $script:sent.query | Should -Be "T | where X == 'y'"
        $script:sent.timespan | Should -Be 'P1D'
        Should -Invoke -ModuleName Norvia.Infra Invoke-Az -ParameterFilter { $Arguments -contains 'https://api.loganalytics.io/v1/workspaces/ws-1/query' }
    }

    It 'returns no rows as an empty array' {
        Mock -ModuleName Norvia.Infra Invoke-Az { '{"tables":[{"rows":[]}]}' }
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId ws-1 -Query 'T'
        , $rows | Should -BeOfType [object[]]
        $rows.Count | Should -Be 0
    }
}

Describe 'The throwaway job' {
    It 'Get-ThrowawayJobName ends in the unix seconds' {
        Get-ThrowawayJobName 'smoke-rating' | Should -Match '^smoke-rating-\d{10,}$'
    }

    It 'New-ThrowawayJob creates the job from a definition file and returns its name' {
        Mock -ModuleName Norvia.Infra Invoke-Az {
            $path = $Arguments[[array]::IndexOf($Arguments, '--yaml') + 1]
            $script:definition = Get-Content -Raw $path | ConvertFrom-Json
        }
        $name = New-ThrowawayJob -Name smoke-x-1 -ResourceGroup rg-test -Definition @{ location = 'westeurope'; properties = @{ template = @{ containers = @(@{ name = 'c' }) } } }
        $name | Should -Be 'smoke-x-1'
        $script:definition.location | Should -Be 'westeurope'
        $script:definition.properties.template.containers[0].name | Should -Be 'c'
    }

    It 'Start-ThrowawayJob waits while the execution runs and returns its status' {
        Mock -ModuleName Norvia.Infra Start-Sleep {}
        $script:reads = 0
        Mock -ModuleName Norvia.Infra Invoke-Az {
            if ($Arguments -contains 'start') { return "exec-1`n" }
            $script:reads++
            if ($script:reads -lt 3) { 'Running' } else { 'Succeeded' }
        }
        $result = Start-ThrowawayJob -Name smoke-x-1 -ResourceGroup rg-test
        $result.Execution | Should -Be 'exec-1'
        $result.Status | Should -Be 'Succeeded'
        $script:reads | Should -Be 3
    }

    It 'Remove-ThrowawayJob tolerates a failing delete' {
        Mock -ModuleName Norvia.Infra az { $global:LASTEXITCODE = 1; 'ResourceNotFound' }
        { Remove-ThrowawayJob -Name smoke-x-1 -ResourceGroup rg-test } | Should -Not -Throw
    }
}
