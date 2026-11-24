#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's TMS (#1017): tms-api and tms-web as Deployments and Services behind the internal-only ingress, each waiting for the
# `tms` migrations Job. The seam is the output of `helm template`, so nothing here needs a cluster. The settings the AppHost gives the two applications
# are compared with the AppHost's source: the chart gives them the same names, apart from the ones it replaces on purpose (connection strings,
# LocalAuth:Allowed, ADR 0053).
BeforeDiscovery {
    $script:helmMissing = -not (Get-Command helm -ErrorAction SilentlyContinue)
}

BeforeAll {
    $script:root = Split-Path $PSScriptRoot -Parent
    $script:chart = Join-Path $script:root 'deploy/helm/norvia'

    function script:Get-Rendered {
        param([string[]] $Set = @())
        $arguments = @('template', 'norvia', $script:chart, '--namespace', 'norvia', '--set', 'sql.saPassword=Test-Passw0rd', '--set', 'edge.baseDomain=norvia.test')
        foreach ($s in $Set) { $arguments += '--set', $s }
        $output = & helm @arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw "helm template failed: $output" }
        ($output -join "`n") -split '(?m)^---\s*$' | Where-Object { $_ -match '(?m)^kind:' } | ForEach-Object {
            [pscustomobject]@{
                Kind = [regex]::Match($_, '(?m)^kind:\s*(\S+)').Groups[1].Value
                Name = [regex]::Match($_, '(?m)^metadata:\s*\n\s+name:\s*"?([^"\s]+)').Groups[1].Value
                Text = $_
            }
        }
    }

    function script:Get-Doc {
        param($Docs, [string] $Kind, [string] $Name)
        $Docs | Where-Object { $_.Kind -eq $Kind -and $_.Name -eq $Name }
    }

    # The container's environment as name -> value (a plain value) or name -> 'secret:<name>/<key>' (a Secret reference).
    function script:Get-Env($Doc) {
        $env = [ordered]@{}
        $pairs = [regex]::Matches($Doc.Text, '(?ms)^\s+- name: (\S+)\n\s+(?:value: (.*?)|valueFrom:\s+secretKeyRef:\s+name: (\S+)\s+key: (\S+))\s*(?=^\s+- name:|^\s+(?:envFrom|ports|readinessProbe|livenessProbe|resources|volumeMounts):|\z)')
        foreach ($m in $pairs) {
            $env[$m.Groups[1].Value] = if ($m.Groups[3].Success) { "secret:$($m.Groups[3].Value)/$($m.Groups[4].Value)" } else { $m.Groups[2].Value.Trim().Trim('"') }
        }
        $env
    }

    # The names of the settings the AppHost gives the application when it runs locally: every `WithEnvironment("Name", value)` of a statement that starts with the
    # application's variable (the declaration, or `variable.WithEnvironment(...)`), except the ones whose value comes from the estate (Azure's, in publish mode).
    function script:Get-AppHostSettings([string] $Variable) {
        $statements = [regex]::Matches($script:appHost, "(?ms)^[ ]*(?:var )?$Variable\b.*?;[ ]*\r?$") | ForEach-Object Value
        $statements | ForEach-Object { [regex]::Matches($_, 'WithEnvironment\("([A-Za-z0-9_]+)",(?>\s*)(?!estate\.|ReferenceExpression)') } | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -notlike 'Agents__*' } | Select-Object -Unique
    }

    $script:docs = Get-Rendered
    $script:appHost = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/AppHost.cs') -Raw
    $script:localTokensSource = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/LocalTokens.cs') -Raw
}

Describe 'the TMS applications' -Skip:$script:helmMissing {
    It '<_> is one replica with a Service on the app port, probed on /health and /alive, and pulls its image from the configured registry' -ForEach 'tms-api', 'tms-web' {
        $deployment = Get-Doc $docs 'Deployment' $_
        $deployment | Should -Not -BeNullOrEmpty
        $deployment.Text | Should -Match '(?m)^\s+replicas: 1$'
        $deployment.Text | Should -Match "image: ${_}:latest"
        $deployment.Text | Should -Match '(?s)readinessProbe:\s+httpGet:\s+path: /health'
        $deployment.Text | Should -Match '(?s)livenessProbe:\s+httpGet:\s+path: /alive'
        $deployment.Text | Should -Match '(?s)limits:\s+memory: \d+Mi'
        (Get-Doc $docs 'Service' $_).Text | Should -Match 'port: 8080'
        $rendered = Get-Rendered @('image.registry=registry.example.test/norvia', 'image.tag=1.2.3')
        (Get-Doc $rendered 'Deployment' $_).Text | Should -Match "image: registry\.example\.test/norvia/${_}:1\.2\.3"
    }

    It '<_> waits for the tms migrations Job, as the AppHost''s WaitForCompletion does' -ForEach 'tms-api', 'tms-web' {
        $text = (Get-Doc $docs 'Deployment' $_).Text
        $text | Should -Match 'serviceAccountName: migrations-waiter'
        $text | Should -Match 'norvia\.io/waits-for-migrations: "true"'
        $text | Should -Match '(?s)initContainers:.*name: wait-for-tms-migrations.*- job/tms-migrations-1\b'
        $text | Should -Not -Match 'wait-for-(?!tms-)\w+-migrations'
    }

    It '<_> is reached through the ingress at its own hostname, which only the LAN or VPN may use' -ForEach 'tms-api', 'tms-web' {
        $ingress = Get-Doc $docs 'Ingress' $_
        $ingress | Should -Not -BeNullOrEmpty
        $ingress.Text | Should -Match "host: ${_}\.norvia\.test"
        $ingress.Text | Should -Match ('(?s)name: ' + $_ + '\s+port:\s+name: http')
        $ingress.Text | Should -Match 'router\.middlewares: norvia-internal-only@kubernetescrd'
    }

    It '<_> sends its telemetry to the dashboard' -ForEach 'tms-api', 'tms-web' {
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $env['OTEL_EXPORTER_OTLP_ENDPOINT'] | Should -Be 'http://aspire-dashboard:18889'
        $env['OTEL_SERVICE_NAME'] | Should -Be $_
    }

    It '<_> reads the tms database as the sa login, from the Secret the migrations use' -ForEach 'tms-api', 'tms-web' {
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $env['MSSQL_SA_PASSWORD'] | Should -Be 'secret:sql/sa-password'
        $env['ConnectionStrings__tms'] | Should -Be 'Server=sql,1433;Database=tms;User ID=sa;Password=$(MSSQL_SA_PASSWORD);TrustServerCertificate=true'
        @($env.Keys).IndexOf('MSSQL_SA_PASSWORD') | Should -BeLessThan @($env.Keys).IndexOf('ConnectionStrings__tms') -Because 'Kubernetes expands only variables defined above'
    }

    It '<_> reads Azurite for Proof of delivery files, as the AppHost''s blob reference says' -ForEach 'tms-api', 'tms-web' {
        (Get-Env (Get-Doc $docs 'Deployment' $_))['ConnectionStrings__blobs'] | Should -Match 'BlobEndpoint=http://azurite:10000/devstoreaccount1;'
    }
}

Describe 'tms-api' -Skip:$script:helmMissing {
    BeforeAll { $script:env = Get-Env (Get-Doc $script:docs 'Deployment' 'tms-api') }

    It 'has every setting the AppHost gives it locally, apart from the ones the chart replaces on purpose' {
        $local = Get-AppHostSettings 'tmsApi'
        $local | Should -Contain 'Seed__DemoData'
        $local | Should -Contain 'Reset__Enabled'
        $local | Should -Contain 'Reset__QuietSubscriptions__0'
        $local | Should -Contain 'IntakeCallers__ClientIdsByChannel__ShipperApi__0'
        $local | Should -Contain 'Authentication__Schemes__Bearer__SigningKeys__0__Issuer'
        foreach ($name in $local) { $env.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
    }

    It 'trusts the local key: the tokens of the install, signed with the Secret local-tokens/signing-key' {
        $env['Authentication__Schemes__Bearer__ValidIssuer'] | Should -Be 'https://norvia.local/tokens'
        $env['Authentication__Schemes__Bearer__SigningKeys__0__Issuer'] | Should -Be 'https://norvia.local/tokens'
        $env['Authentication__Schemes__Bearer__ValidAudiences__0'] | Should -Be 'api://norvia-tms'
        $env['Authentication__Schemes__Bearer__SigningKeys__0__Value'] | Should -Be 'secret:local-tokens/signing-key'
        $env['IntakeCallers__ClientIdsByChannel__ShipperApi__0'] | Should -Be 'norvia-local-intake-logic-app'
    }

    It 'takes each token constant as LocalTokens.cs has it' {
        foreach ($constant in 'TmsAudience', 'IntakeLogicAppClientId') {
            [regex]::Match($localTokensSource, "public const string $constant = ""([^""]+)""").Groups[1].Value | Should -BeIn $env.Values
        }
    }

    It 'is allowed to trust that key where there is no identity provider: LocalAuth:Allowed' {
        $env['LocalAuth__Allowed'] | Should -Be 'true'
    }

    It 'quiets the subscriptions the AppHost does before a reset, and allows the reset' {
        $quiet = [regex]::Matches($appHost, 'tmsApi\.WithEnvironment\("Reset__QuietSubscriptions__0".*?;', 'Singleline').Value
        foreach ($m in [regex]::Matches($quiet, '"(Reset__QuietSubscriptions__\d)", "([^"]+)"')) {
            $env[$m.Groups[1].Value] | Should -Be $m.Groups[2].Value
        }
        @($env.Keys | Where-Object { $_ -like 'Reset__QuietSubscriptions__*' }).Count | Should -Be 4
        $env['Reset__Enabled'] | Should -Be 'true'
    }

    It 'reads the seed file the image carries, and books the demo''s days on its first start' {
        $env['Seed__Path'] | Should -Be 'seed/norvia.seed.json'
        $env['Seed__DemoData'] | Should -Be 'True'
    }

    It 'sends to the first Service Bus emulator, as the AppHost''s messaging reference does' {
        $env['ConnectionStrings__messaging'] | Should -Match '^Endpoint=sb://messaging;'
    }

    It 'is not given the settings of Azure: no Entra authority, no as-found toggle, no Foundry' {
        $env.Keys | Where-Object { $_ -match 'Authority|Foundry|Entra|APPLICATIONINSIGHTS' } | Should -BeNullOrEmpty
    }
}

Describe 'tms-web' -Skip:$script:helmMissing {
    BeforeAll { $script:env = Get-Env (Get-Doc $script:docs 'Deployment' 'tms-web') }

    It 'has every setting the AppHost gives it locally' {
        $local = Get-AppHostSettings 'tmsWeb'
        $local | Should -Contain 'TmsApi__BaseAddress'
        $local | Should -Contain 'TmsApi__LocalTokens__Issuer'
        foreach ($name in $local) { $env.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
    }

    It 'calls the TMS API by its Service name, as the local operator: its own token, signed with the key of the install' {
        $env['TmsApi__BaseAddress'] | Should -Be 'http://tms-api:8080'
        $env['TmsApi__LocalTokens__Issuer'] | Should -Be 'https://norvia.local/tokens'
        $env['TmsApi__LocalTokens__Audience'] | Should -Be 'api://norvia-tms'
        $env['TmsApi__LocalTokens__ClientId'] | Should -Be ([regex]::Match($localTokensSource, 'public const string TmsUiClientId = "([^"]+)"').Groups[1].Value)
        $env['TmsApi__LocalTokens__SigningKey'] | Should -Be 'secret:local-tokens/signing-key'
    }

    It 'is allowed to run the local sign-in where there is no identity provider: LocalAuth:Allowed' {
        $env['LocalAuth__Allowed'] | Should -Be 'true'
    }

    It 'links a Consignee to the Partner portal at the address the AppHost gives it, which is the in-cluster one until the portal has an ingress' {
        $env['PartnerPortal__BaseAddress'] | Should -Be 'http://partner-portal-web:8080'
    }

    It 'reads the seed file the image carries' {
        $env['Seed__Path'] | Should -Be 'seed/norvia.seed.json'
    }

    It 'is not given the settings of Azure: no Entra scope, no managed identity, no Foundry' {
        $env.Keys | Where-Object { $_ -match 'Entra|Foundry|ManagedIdentity|APPLICATIONINSIGHTS' } | Should -BeNullOrEmpty
        $env.Contains('TmsApi__Scope') | Should -BeFalse
    }
}

Describe 'LocalAuth:Allowed' -Skip:$script:helmMissing {
    It 'is a value of the chart, which the operator turns off' {
        $off = Get-Rendered @('localAuth.allowed=false')
        foreach ($name in 'tms-api', 'tms-web') {
            (Get-Env (Get-Doc $off 'Deployment' $name))['LocalAuth__Allowed'] | Should -Be 'false'
        }
    }
}

Describe 'the TMS in the rest of the chart' -Skip:$script:helmMissing {
    It 'is let through by the network policy from the ingress controller, as the other exposed applications are' {
        $policy = (Get-Doc $docs 'NetworkPolicy' 'allow-ingress-controller').Text
        $policy | Should -Match '- tms-api'
        $policy | Should -Match '- tms-web'
    }

    It 'is called by the workers and the Partner API at the Service name tms-api, which is the Service' {
        (Get-Doc $docs 'Service' 'tms-api') | Should -Not -BeNullOrEmpty
        (Get-Env (Get-Doc $docs 'Deployment' 'partner-api'))['Tms__BaseAddress'] | Should -Be 'http://tms-api:8080'
        (Get-Env (Get-Doc $docs 'Deployment' 'booking-worker'))['Booking__TmsBaseAddress'] | Should -Be 'http://tms-api:8080'
    }
}
