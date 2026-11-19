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

Describe 'Test-Status' {
    It 'ignores case, spaces and hyphens' {
        Test-Status -Actual 'InTransit' -Expected 'In transit' | Should -BeTrue
        Test-Status -Actual 'in-transit' -Expected 'In transit' | Should -BeTrue
    }

    It 'tells two statuses apart' {
        Test-Status -Actual 'Collected' -Expected 'Delivered' | Should -BeFalse
    }

    It 'is false for nothing' {
        Test-Status -Actual $null -Expected 'Delivered' | Should -BeFalse
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
