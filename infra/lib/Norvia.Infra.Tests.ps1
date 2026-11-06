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

Describe 'Get-RequiredEnvironmentVariable' {
    It 'returns the value' {
        $env:NORVIA_TEST_SET = 'x'
        try { Get-RequiredEnvironmentVariable NORVIA_TEST_SET | Should -Be 'x' } finally { Remove-Item Env:NORVIA_TEST_SET }
    }

    It 'fails with the message when it is not set' {
        { Get-RequiredEnvironmentVariable NORVIA_TEST_UNSET 'Deploy the estate first' } | Should -Throw 'FAIL  Deploy the estate first'
    }
}

Describe 'Invoke-AzRest' {
    It 'sends the body in a file and parses the response' {
        Mock -ModuleName Norvia.Infra Invoke-Az { '{"value":[1,2]}' }
        (Invoke-AzRest -Method post -Url https://example/x -Body @{ a = 1 }).value | Should -Be 1, 2
        Should -Invoke -ModuleName Norvia.Infra Invoke-Az -ParameterFilter {
            $Arguments -contains '--method' -and $Arguments -contains 'post' -and ($Arguments | Where-Object { $_ -like '--body' }) -and ($Arguments -like '@*')
        }
    }

    It 'returns nothing for an empty response' {
        Mock -ModuleName Norvia.Infra Invoke-Az { '' }
        Invoke-AzRest -Method get -Url https://example/x | Should -BeNullOrEmpty
    }
}

Describe 'Publish-LogicApp' {
    BeforeEach {
        Mock -ModuleName Norvia.Infra Write-Host { }
        Mock -ModuleName Norvia.Infra Start-Sleep { }
        Mock -ModuleName Norvia.Infra Invoke-Az { }
        Mock -ModuleName Norvia.Infra dotnet { $global:LASTEXITCODE = 0 }
    }

    It 'sets the settings, builds the functions, stages the zip and deploys it' {
        Publish-LogicApp -LogicApp logic-x -Title 'the X Logic App' -Project src/Integration/BillingLogicApp -Include host.json `
            -Settings @{ TMS_BASE_URL = 'https://tms' } -FunctionsProject src/Integration/Norvia.Integration.CarrierBooking.Functions `
            -ResourceGroup rg-test -Subscription sub
        Should -Invoke -ModuleName Norvia.Infra Invoke-Az -ParameterFilter { $Arguments -contains 'appsettings' -and $Arguments -contains 'TMS_BASE_URL=https://tms' }
        Should -Invoke -ModuleName Norvia.Infra dotnet -Times 1
        Should -Invoke -ModuleName Norvia.Infra Invoke-Az -ParameterFilter { $Arguments -contains 'config-zip' -and ($Arguments -like '*billing*.zip') }
    }

    It 'deploys again after a failure, and gives up after the third' {
        Mock -ModuleName Norvia.Infra Invoke-Az { throw 'restarting' } -ParameterFilter { $Arguments -contains 'config-zip' }
        { Publish-LogicApp -LogicApp logic-x -Title 't' -Project src/Integration/BillingLogicApp -Include host.json -ResourceGroup rg-test } | Should -Throw '*restarting*'
        Should -Invoke -ModuleName Norvia.Infra Invoke-Az -Times 3 -ParameterFilter { $Arguments -contains 'config-zip' }
        Should -Invoke -ModuleName Norvia.Infra Start-Sleep -Times 2
    }
}

Describe 'Get-ThrowawayJobDefinition' {
    BeforeEach {
        Mock -ModuleName Norvia.Infra Invoke-Az { "/subscriptions/s/environments/env`n" }
    }

    It 'runs the calls as the identity in the estate environment, with the variables as env entries' {
        $definition = Get-ThrowawayJobDefinition -Name smoke-x -Calls 'echo hi' -IdentityId '/id/one' `
            -Environment ([ordered]@{ TMS = 'https://tms' }) -Location westeurope -ResourceGroup rg-test
        $definition.properties.environmentId | Should -Be '/subscriptions/s/environments/env'
        $definition.identity.userAssignedIdentities.Keys | Should -Contain '/id/one'
        $container = $definition.properties.template.containers[0]
        $container.name | Should -Be 'smoke-x'
        $container.command | Should -Be @('/bin/sh', '-c', 'echo hi')
        $container.env[0].name | Should -Be 'TMS'
        $container.env[0].value | Should -Be 'https://tms'
    }
}

Describe 'Get-ThrowawayJobResult' {
    It 'strips the marker from the first line' {
        Mock -ModuleName Norvia.Infra Invoke-LogAnalyticsQuery { , @(, @('SMOKE-RESULT {"a":1}')) }
        Get-ThrowawayJobResult -WorkspaceId w -JobName j | Should -Be '{"a":1}'
    }

    It 'returns $null when no line has arrived' {
        Mock -ModuleName Norvia.Infra Invoke-LogAnalyticsQuery { , @() }
        Get-ThrowawayJobResult -WorkspaceId w -JobName j | Should -BeNullOrEmpty
    }
}

Describe 'Test-SameValues' {
    It 'is true for the same values in the same order, and for null and an empty list' {
        Test-SameValues @('a', 'b') @('a', 'b') | Should -BeTrue
        Test-SameValues $null @() | Should -BeTrue
    }

    It 'is false for other values or another order' {
        Test-SameValues @('a', 'b') @('b', 'a') | Should -BeFalse
        Test-SameValues @('a') @() | Should -BeFalse
    }
}

Describe 'Get-TrailSummary' {
    It 'returns $null while the trail has no rows' {
        Mock -ModuleName Norvia.Infra Invoke-LogAnalyticsQuery { , @() }
        Get-TrailSummary -WorkspaceId w -CorrelationId c -Keys shipmentNumber | Should -BeNullOrEmpty
    }

    It 'names each column after its key' {
        Mock -ModuleName Norvia.Infra Invoke-LogAnalyticsQuery { , @(, @('3', '["b","a"]', '["Succeeded"]', '["SHP-1"]', '["publish X"]')) }
        $summary = Get-TrailSummary -WorkspaceId w -CorrelationId c -Keys shipmentNumber
        $summary.flows | Should -Be @('a', 'b')
        $summary.shipmentNumber | Should -Be @('SHP-1')
        $summary.published | Should -Be @('publish X')
    }
}

Describe 'Invoke-ThrowawayJobForResult' {
    BeforeEach {
        Mock -ModuleName Norvia.Infra Get-ThrowawayJobDefinition { @{} }
        Mock -ModuleName Norvia.Infra New-ThrowawayJob { }
        Mock -ModuleName Norvia.Infra Remove-ThrowawayJob { }
        Mock -ModuleName Norvia.Infra Get-LogAnalyticsWorkspaceId { 'w' }
    }

    It 'returns the parsed result and deletes the job' {
        Mock -ModuleName Norvia.Infra Start-ThrowawayJob { [pscustomobject]@{ Execution = 'e'; Status = 'Succeeded' } }
        Mock -ModuleName Norvia.Infra Get-ThrowawayJobResult { '{"a":1}' }
        (Invoke-ThrowawayJobForResult -Prefix smoke-x -Calls 'x' -IdentityId i -ResourceGroup rg -WorkspaceName n).a | Should -Be 1
        Should -Invoke -ModuleName Norvia.Infra Remove-ThrowawayJob -Times 1
    }

    It 'fails with the status when the execution does not succeed, and still deletes the job' {
        Mock -ModuleName Norvia.Infra Start-ThrowawayJob { [pscustomobject]@{ Execution = 'e1'; Status = 'Failed' } }
        { Invoke-ThrowawayJobForResult -Prefix smoke-x -Calls 'x' -IdentityId i -Purpose 'reading the TMS' -ResourceGroup rg -WorkspaceName n } |
            Should -Throw '*reading the TMS ended Failed*e1*'
        Should -Invoke -ModuleName Norvia.Infra Remove-ThrowawayJob -Times 1
    }
}

Describe 'Get-ThrowawayJobDefinition secrets and identities' {
    It 'keeps a secret out of the variables and refers to it by name' {
        Mock -ModuleName Norvia.Infra Invoke-Az { 'env' }
        $definition = Get-ThrowawayJobDefinition -Name n -Calls c -IdentityId '/id/one', '/id/two' `
            -Secrets ([ordered]@{ VAN_DIJK_KEY = 'hunter2' }) -Location westeurope -ResourceGroup rg
        $definition.identity.userAssignedIdentities.Keys.Count | Should -Be 2
        $definition.properties.configuration.secrets[0].name | Should -Be 'van-dijk-key'
        $variable = $definition.properties.template.containers[0].env | Where-Object name -EQ 'VAN_DIJK_KEY'
        $variable.secretRef | Should -Be 'van-dijk-key'
        $variable.ContainsKey('value') | Should -BeFalse
    }
}

Describe 'Assert-InApplicationInsights' {
    It 'passes when every app is found' {
        Mock -ModuleName Norvia.Infra Invoke-LogAnalyticsQuery { , @(, @('[env]/tms-api'), , @('apim-x')) }
        Assert-InApplicationInsights -WorkspaceId w -What 'PU' -Value v -Roles tms-api, apim-x 6>&1 | Should -BeLike 'PASS  PU is in Application Insights*'
    }

    It 'fails naming the app that is missing' {
        Mock -ModuleName Norvia.Infra Invoke-LogAnalyticsQuery { , @(, @('apim-x')) }
        Mock -ModuleName Norvia.Infra Start-Sleep { }
        { Assert-InApplicationInsights -WorkspaceId w -What 'PU' -Value v -Roles tms-api, apim-x } | Should -Throw '*for tms-api after 10 minutes'
    }
}

Describe 'Get-PersonaClientId' {
    It 'finds a Persona in the semicolon list' {
        $env:AZURE_ESTATE_PERSONA_CLIENT_IDS = 'kessler=aaa;van-dijk-transport=bbb'
        try {
            Get-PersonaClientId van-dijk-transport | Should -Be 'bbb'
            Get-PersonaClientId nobody | Should -BeNullOrEmpty
        }
        finally { $env:AZURE_ESTATE_PERSONA_CLIENT_IDS = $null }
    }
}

Describe 'Get-MetricAlertValue' {
    It 'returns $null when the rule does not exist' {
        Mock -ModuleName Norvia.Infra Invoke-Az { $null }
        Get-MetricAlertValue alert-x name -ResourceGroup rg | Should -BeNullOrEmpty
    }

    It 'returns the trimmed value' {
        Mock -ModuleName Norvia.Infra Invoke-Az { "true`n" }
        Get-MetricAlertValue alert-x enabled -ResourceGroup rg | Should -Be 'true'
    }
}

Describe 'Get-QueueDeadLetterCount' {
    It 'returns the count as a number' {
        Mock -ModuleName Norvia.Infra Invoke-Az { '3' }
        Get-QueueDeadLetterCount -Namespace n -Queue q -ResourceGroup rg | Should -Be 3
    }
}
