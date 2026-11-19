#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'Norvia.Infra.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'Norvia.Kubernetes.psm1') -Force

    function script:Read-Jwt([string] $Token) {
        $parts = $Token -split '\.'
        $decode = {
            param($Part)
            $padded = $Part.Replace('-', '+').Replace('_', '/')
            $padded += '=' * ((4 - $padded.Length % 4) % 4)
            [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($padded)) | ConvertFrom-Json -AsHashtable
        }
        @{ Header = & $decode $parts[0]; Payload = & $decode $parts[1]; Signature = $parts[2]; Signed = "$($parts[0]).$($parts[1])" }
    }
}

Describe 'Get-LocalToken' {
    BeforeAll {
        # What the chart stores in local-tokens/signing-key: a base64 string, which the apps decode again into the key's bytes.
        $script:keyBytes = [byte[]](1..32)
        $script:signingKey = [Convert]::ToBase64String($script:keyBytes)
        $script:token = Get-LocalToken -SigningKey $script:signingKey -Issuer 'https://norvia.local/tokens' -Audience 'api://norvia-partner-api' `
            -ClientId 'norvia-local-persona-van-dijk-transport' -Roles 'Tracking.Submit', 'ProofsOfDelivery.Submit'
        $script:jwt = Read-Jwt $script:token
    }

    It 'is an HS256 JWT' {
        $script:jwt.Header.alg | Should -Be 'HS256'
        $script:jwt.Header.typ | Should -Be 'JWT'
    }

    It 'carries the issuer, the audience, the client in azp and the roles' {
        $script:jwt.Payload.iss | Should -Be 'https://norvia.local/tokens'
        $script:jwt.Payload.aud | Should -Be 'api://norvia-partner-api'
        $script:jwt.Payload.azp | Should -Be 'norvia-local-persona-van-dijk-transport'
        $script:jwt.Payload.roles | Should -Be @('Tracking.Submit', 'ProofsOfDelivery.Submit')
    }

    It 'is valid now and for an hour' {
        $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $script:jwt.Payload.nbf | Should -BeLessOrEqual $now
        $script:jwt.Payload.exp - $script:jwt.Payload.nbf | Should -Be 3600
    }

    It 'is signed with the key the secret holds, decoded once' {
        $hmac = [Security.Cryptography.HMACSHA256]::new($script:keyBytes)
        $expected = [Convert]::ToBase64String($hmac.ComputeHash([Text.Encoding]::ASCII.GetBytes($script:jwt.Signed))).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        $script:jwt.Signature | Should -Be $expected
    }

    It 'leaves the roles claim out of a token with no role, as Entra does' {
        $none = Read-Jwt (Get-LocalToken -SigningKey $script:signingKey -Issuer 'i' -Audience 'a' -ClientId 'c')
        $none.Payload.ContainsKey('roles') | Should -BeFalse
    }
}

Describe 'Get-IngressUrl' {
    It 'is https at the application name under the base domain' {
        Get-IngressUrl -Application 'partner-api' -BaseDomain 'norvia.example.org' | Should -Be 'https://partner-api.norvia.example.org'
    }

    It 'is http when TLS is off' {
        Get-IngressUrl -Application 'tms-api' -BaseDomain 'norvia.test' -Insecure | Should -Be 'http://tms-api.norvia.test'
    }

    It 'refuses an empty base domain' {
        { Get-IngressUrl -Application 'partner-api' -BaseDomain '' } | Should -Throw
    }
}

Describe 'Test-SameStatus' {
    It 'ignores case, spaces and hyphens' {
        Test-SameStatus -Actual 'InTransit' -Expected 'In transit' | Should -BeTrue
        Test-SameStatus -Actual 'in-transit' -Expected 'In transit' | Should -BeTrue
    }

    It 'tells two statuses apart' {
        Test-SameStatus -Actual 'Collected' -Expected 'Delivered' | Should -BeFalse
    }

    It 'is false for nothing' {
        Test-SameStatus -Actual $null -Expected 'Delivered' | Should -BeFalse
    }
}

Describe 'Invoke-SmokeStep' {
    It 'prints PASS with the step and returns what the step returned' {
        $result = Invoke-SmokeStep -Name 'Book' -Action { 42 } 6>&1
        $result | Should -Contain 42
        ($result | Where-Object { $_ -is [Management.Automation.InformationRecord] }).MessageData.Message | Should -Match 'PASS\s+Book'
    }

    It 'names the step that broke and why' {
        { Invoke-SmokeStep -Name 'Release the Shipment' -Action { throw 'the TMS answered 503' } } |
            Should -Throw 'FAIL  Release the Shipment: the TMS answered 503'
    }
}

Describe 'Wait-ForValue' {
    It 'returns the first value the condition gives' {
        $script:calls = 0
        $value = Wait-ForValue -Description 'a thing' -Condition { $script:calls++; if ($script:calls -ge 3) { 'ready' } } -Attempts 5 -IntervalSeconds 0
        $value | Should -Be 'ready'
        $script:calls | Should -Be 3
    }

    It 'fails naming what it waited for' {
        { Wait-ForValue -Description 'the Shipment to be Booked' -Condition { $null } -Attempts 2 -IntervalSeconds 0 } |
            Should -Throw '*the Shipment to be Booked*'
    }

    It 'reports the last thing it saw' {
        { Wait-ForValue -Description 'x' -Condition { $null } -Describe { 'status was Planned' } -Attempts 1 -IntervalSeconds 0 } |
            Should -Throw '*status was Planned*'
    }
}

Describe 'Format-Answer' {
    It 'says the status alone when there is no problem' {
        Format-Answer ([pscustomobject]@{ Status = 503; Body = $null }) | Should -Be 'answered 503'
    }

    It 'adds the title, code, text and message of a problem, never the rest of the body' {
        $answer = [pscustomobject]@{ Status = 409; Body = @{ title = 'Conflict'; code = 'TransportOrderAlreadyPlanned'; secret = 'hunter2' } }
        Format-Answer $answer | Should -Be 'answered 409 (Conflict, TransportOrderAlreadyPlanned)'
    }
}

Describe 'Assert-Accepted' {
    It 'passes a 2xx' {
        { Assert-Accepted ([pscustomobject]@{ Status = 201; Body = $null; CorrelationId = $null }) -What 'the Partner API' } | Should -Not -Throw
    }

    It 'refuses anything else, saying what answered' {
        { Assert-Accepted ([pscustomobject]@{ Status = 401; Body = @{ message = 'bad key' }; CorrelationId = $null }) -What 'the Partner API' } |
            Should -Throw 'the Partner API answered 401 (bad key)'
    }

    It 'with -Status 202 refuses a 200' {
        { Assert-Accepted ([pscustomobject]@{ Status = 200; Body = $null; CorrelationId = 'c' }) -What 'the Partner API' -Status 202 } |
            Should -Throw '*not 202 Accepted*'
    }

    It 'with -RequireCorrelationId refuses an answer without one' {
        { Assert-Accepted ([pscustomobject]@{ Status = 202; Body = $null; CorrelationId = $null }) -What 'the Partner API' -Status 202 -RequireCorrelationId } |
            Should -Throw '*X-Correlation-ID*'
    }
}

Describe 'Invoke-Call' {
    It 'returns the status, the parsed body and the correlation ID without throwing for an error status' {
        Mock Invoke-WebRequest -ModuleName Norvia.Kubernetes {
            [pscustomobject]@{ StatusCode = 409; Content = '{"code":"X"}'; Headers = @{ 'X-Correlation-ID' = @('abc') } }
        }
        $answer = Invoke-Call -Method GET -Uri 'https://x.test/a'
        $answer.Status | Should -Be 409
        $answer.Body.code | Should -Be 'X'
        $answer.CorrelationId | Should -Be 'abc'
        Should -Invoke Invoke-WebRequest -ModuleName Norvia.Kubernetes -ParameterFilter { $SkipHttpErrorCheck -and -not $SkipCertificateCheck }
    }

    It 'sends the body as JSON and can skip the certificate check' {
        Mock Invoke-WebRequest -ModuleName Norvia.Kubernetes { [pscustomobject]@{ StatusCode = 202; Content = ''; Headers = @{} } }
        $answer = Invoke-Call -Method POST -Uri 'https://x.test/a' -Body @{ a = 1 } -SkipCertificateCheck
        $answer.Body | Should -BeNullOrEmpty
        Should -Invoke Invoke-WebRequest -ModuleName Norvia.Kubernetes -ParameterFilter { $Body -eq '{"a":1}' -and $ContentType -eq 'application/json' -and $SkipCertificateCheck }
    }
}

Describe 'Get-SecretValue' {
    BeforeAll {
        # kubectl is on the dev container but not on every runner; Pester can only mock a command that exists.
        $script:kubectlStub = -not (Get-Command kubectl -ErrorAction SilentlyContinue)
        if ($script:kubectlStub) { function global:kubectl { throw 'kubectl is a stub for the Pester mocks' } }
    }
    AfterAll { if ($script:kubectlStub) { Remove-Item Function:\kubectl -ErrorAction SilentlyContinue } }

    It 'decodes the Secret key' {
        Mock Invoke-Kubectl -ModuleName Norvia.Kubernetes { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('s3cret')) }
        Get-SecretValue -Namespace 'norvia' -Secret 'local-tokens' -Key 'signing-key' | Should -Be 's3cret'
        Should -Invoke Invoke-Kubectl -ModuleName Norvia.Kubernetes -ParameterFilter { ($Arguments -join ' ') -match 'get secret local-tokens --namespace norvia' }
    }

    It 'fails when the key is empty' {
        Mock Invoke-Kubectl -ModuleName Norvia.Kubernetes { '' }
        { Get-SecretValue -Namespace 'norvia' -Secret 'local-tokens' -Key 'nope' } | Should -Throw '*has no key nope*'
    }

    It 'Invoke-Kubectl fails with kubectl''s own message when it exits non-zero' {
        Mock kubectl -ModuleName Norvia.Kubernetes { $global:LASTEXITCODE = 1; 'Forbidden' }
        { Invoke-Kubectl get secret x } | Should -Throw '*Forbidden*'
    }
}

Describe 'Get-IsolationTarget' {
    It 'names the Partner API, SQL Server and the two Service Bus emulators by their address from another namespace' {
        Get-IsolationTarget -Namespace 'norvia' | Should -Be @(
            'partner-api.norvia.svc.cluster.local:8080'
            'sql.norvia.svc.cluster.local:1433'
            'messaging.norvia.svc.cluster.local:5672'
            'messaging-2.norvia.svc.cluster.local:5672'
        )
    }

    It 'follows the namespace' {
        Get-IsolationTarget -Namespace 'sandbox-2' | Select-Object -First 1 | Should -Be 'partner-api.sandbox-2.svc.cluster.local:8080'
    }
}

Describe 'Assert-Isolated' {
    BeforeAll {
        $script:targets = 'partner-api.norvia.svc.cluster.local:8080', 'sql.norvia.svc.cluster.local:1433'
    }

    It 'passes when a pod of the namespace connects and a pod of the other namespace does not' {
        Mock Test-CanConnect -ModuleName Norvia.Kubernetes { $Target -like 'kube-dns.*' -or $Namespace -eq 'norvia' }
        { Assert-Isolated -Namespace 'norvia' -ProbeNamespace 'elsewhere' -Target $targets -Image 'probe:1' } | Should -Not -Throw
        Should -Invoke Test-CanConnect -ModuleName Norvia.Kubernetes -ParameterFilter { $Namespace -eq 'elsewhere' -and $Target -eq 'sql.norvia.svc.cluster.local:1433' }
    }

    It 'fails naming every target the other namespace reaches' {
        Mock Test-CanConnect -ModuleName Norvia.Kubernetes { $Target -like 'kube-dns.*' -or $Namespace -eq 'norvia' -or $Target -like 'sql.*' }
        { Assert-Isolated -Namespace 'norvia' -ProbeNamespace 'elsewhere' -Target $targets -Image 'probe:1' } |
            Should -Throw '*sql.norvia.svc.cluster.local:1433*' -ExceptionType ([System.Management.Automation.RuntimeException])
    }

    It 'does not name a target the other namespace does not reach' {
        Mock Test-CanConnect -ModuleName Norvia.Kubernetes { $Target -like 'kube-dns.*' -or $Namespace -eq 'norvia' -or $Target -like 'sql.*' }
        $message = try { Assert-Isolated -Namespace 'norvia' -ProbeNamespace 'elsewhere' -Target $targets -Image 'probe:1' } catch { $_.Exception.Message }
        $message | Should -Not -Match 'partner-api'
    }

    It 'cannot tell, and says so, when the probe does not start in the other namespace (a pull that fails there is not a refusal)' {
        Mock Test-CanConnect -ModuleName Norvia.Kubernetes { $Namespace -eq 'norvia' }
        { Assert-Isolated -Namespace 'norvia' -ProbeNamespace 'elsewhere' -Target $targets -Image 'probe:1' } | Should -Throw '*could not connect from the namespace elsewhere to kube-dns*'
        Should -Invoke Test-CanConnect -ModuleName Norvia.Kubernetes -Times 0 -ParameterFilter { $Namespace -eq 'elsewhere' -and $Target -like 'sql.*' }
    }

    It 'cannot tell, and says so, when the probe does not connect from inside the namespace either' {
        Mock Test-CanConnect -ModuleName Norvia.Kubernetes { $false }
        { Assert-Isolated -Namespace 'norvia' -ProbeNamespace 'elsewhere' -Target $targets -Image 'probe:1' } | Should -Throw '*could not connect from inside the namespace*'
        Should -Invoke Test-CanConnect -ModuleName Norvia.Kubernetes -Times 0 -ParameterFilter { $Namespace -eq 'elsewhere' }
    }
}

Describe 'Test-CanConnect' {
    BeforeAll {
        $script:kubectlStub = -not (Get-Command kubectl -ErrorAction SilentlyContinue)
        if ($script:kubectlStub) { function global:kubectl { throw 'kubectl is a stub for the Pester mocks' } }
    }
    AfterAll { if ($script:kubectlStub) { Remove-Item Function:\kubectl -ErrorAction SilentlyContinue } }

    It 'runs a throwaway pod of the image in the namespace, which dials the target' {
        Mock kubectl -ModuleName Norvia.Kubernetes { $global:LASTEXITCODE = 0 }
        Test-CanConnect -Namespace 'elsewhere' -Target 'sql.norvia.svc.cluster.local:1433' -Image 'probe:1' -TimeoutSeconds 4 | Should -BeTrue
        Should -Invoke kubectl -ModuleName Norvia.Kubernetes -ParameterFilter {
            $line = $args -join ' '
            $line -match '^run connect-probe-\w+ --namespace elsewhere --image probe:1 --restart=Never --rm -i --quiet' -and $line -match '/agnhost connect sql\.norvia\.svc\.cluster\.local:1433 --timeout=4s$'
        }
    }

    It 'is false when the dial fails' {
        Mock kubectl -ModuleName Norvia.Kubernetes { $global:LASTEXITCODE = 1; 'TIMEOUT' }
        Test-CanConnect -Namespace 'elsewhere' -Target 'sql.norvia.svc.cluster.local:1433' -Image 'probe:1' | Should -BeFalse
    }
}
