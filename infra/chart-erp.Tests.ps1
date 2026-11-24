#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's ERP (#1018): erp-api and erp-web as Deployments and Services behind the internal-only ingress, each waiting for the
# `erp` migrations Job. The seam is the output of `helm template`, so nothing here needs a cluster. The settings the AppHost gives the two applications
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

Describe 'the ERP applications' -Skip:$script:helmMissing {
    It '<_> is one replica with a Service on the app port, probed on /health and /alive, and pulls its image from the configured registry' -ForEach 'erp-api', 'erp-web' {
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

    It '<_> waits for the erp migrations Job, and only that one, as the AppHost''s WaitForCompletion does' -ForEach 'erp-api', 'erp-web' {
        $text = (Get-Doc $docs 'Deployment' $_).Text
        $text | Should -Match 'serviceAccountName: migrations-waiter'
        $text | Should -Match 'norvia\.io/waits-for-migrations: "true"'
        $text | Should -Match '(?s)initContainers:.*name: wait-for-erp-migrations.*- job/erp-migrations-1\b'
        $text | Should -Not -Match 'wait-for-(?!erp-)\w+-migrations'
    }

    It '<_> is reached through the ingress at its own hostname, which only the LAN or VPN may use' -ForEach 'erp-api', 'erp-web' {
        $ingress = Get-Doc $docs 'Ingress' $_
        $ingress | Should -Not -BeNullOrEmpty
        $ingress.Text | Should -Match "host: ${_}\.norvia\.test"
        $ingress.Text | Should -Match ('(?s)name: ' + $_ + '\s+port:\s+name: http')
        $ingress.Text | Should -Match 'router\.middlewares: norvia-internal-only@kubernetescrd'
    }

    It '<_> sends its telemetry to the dashboard' -ForEach 'erp-api', 'erp-web' {
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $env['OTEL_EXPORTER_OTLP_ENDPOINT'] | Should -Be 'http://aspire-dashboard:18889'
        $env['OTEL_SERVICE_NAME'] | Should -Be $_
    }

    It '<_> reads the erp database as the sa login, from the Secret the migrations use' -ForEach 'erp-api', 'erp-web' {
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $env['MSSQL_SA_PASSWORD'] | Should -Be 'secret:sql/sa-password'
        $env['ConnectionStrings__erp'] | Should -Be 'Server=sql,1433;Database=erp;User ID=sa;Password=$(MSSQL_SA_PASSWORD);TrustServerCertificate=true'
        @($env.Keys).IndexOf('MSSQL_SA_PASSWORD') | Should -BeLessThan @($env.Keys).IndexOf('ConnectionStrings__erp') -Because 'Kubernetes expands only variables defined above'
    }

    It '<_> is allowed to run its local sign-in or trust the local key where there is no identity provider: LocalAuth:Allowed' -ForEach 'erp-api', 'erp-web' {
        (Get-Env (Get-Doc $docs 'Deployment' $_))['LocalAuth__Allowed'] | Should -Be 'true'
        (Get-Env (Get-Doc (Get-Rendered @('localAuth.allowed=false')) 'Deployment' $_))['LocalAuth__Allowed'] | Should -Be 'false'
    }
}

Describe 'erp-api' -Skip:$script:helmMissing {
    BeforeAll { $script:env = Get-Env (Get-Doc $script:docs 'Deployment' 'erp-api') }

    It 'has every setting the AppHost gives it locally, apart from the ones the chart replaces on purpose' {
        $local = Get-AppHostSettings 'erpApi'
        $local | Should -Contain 'Reset__Enabled'
        $local | Should -Contain 'Reset__QuietSubscriptions__0'
        $local | Should -Contain 'Authentication__Schemes__Bearer__ValidAudiences__0'
        foreach ($name in $local) { $env.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
    }

    It 'trusts the local key: the tokens of the install, signed with the Secret local-tokens/signing-key' {
        $env['Authentication__Schemes__Bearer__ValidIssuer'] | Should -Be 'https://norvia.local/tokens'
        $env['Authentication__Schemes__Bearer__SigningKeys__0__Issuer'] | Should -Be 'https://norvia.local/tokens'
        $env['Authentication__Schemes__Bearer__ValidAudiences__0'] | Should -Be 'api://norvia-erp'
        $env['Authentication__Schemes__Bearer__SigningKeys__0__Value'] | Should -Be 'secret:local-tokens/signing-key'
    }

    It 'takes its audience as LocalTokens.cs has it' {
        [regex]::Match($localTokensSource, 'public const string ErpAudience = "([^"]+)"').Groups[1].Value | Should -BeIn $env.Values
    }

    It 'reaches both Service Bus emulators, as the AppHost does locally: messaging, and messaging-edge for the Edge topics' {
        $env['ConnectionStrings__messaging'] | Should -Match '^Endpoint=sb://messaging;'
        $env['ConnectionStrings__messaging-edge'] | Should -Match '^Endpoint=sb://messaging-2;'
    }

    It 'quiets the subscriptions the AppHost does before a reset, with the Edge ones in the namespace of their own, and allows the reset' {
        $env['Reset__QuietSubscriptions__0'] | Should -Be 'transport-order-events/billing'
        $env['Reset__QuietSubscriptions__1'] | Should -Be 'shipment-events/carrier-cost'
        $env['Reset__QuietSubscriptions__2'] | Should -Be 'messaging-edge/invoicing-events/shipper-invoice'
        $env['Reset__QuietSubscriptions__3'] | Should -Be 'messaging-edge/invoicing-events/carrier-invoice-outcome'
        @($env.Keys | Where-Object { $_ -like 'Reset__QuietSubscriptions__*' }).Count | Should -Be 4
        $env['Reset__Enabled'] | Should -Be 'true'
    }

    It 'is not given the settings of Azure: no Entra authority, no Foundry, no Application Insights, no Agents' {
        $env.Keys | Where-Object { $_ -match 'Authority|Foundry|Entra|APPLICATIONINSIGHTS|^Agents__' } | Should -BeNullOrEmpty
    }
}

Describe 'erp-web' -Skip:$script:helmMissing {
    BeforeAll { $script:env = Get-Env (Get-Doc $script:docs 'Deployment' 'erp-web') }

    It 'has every setting the AppHost gives it locally' {
        $local = Get-AppHostSettings 'erpWeb' | Where-Object { $_ -notlike '*__Entra__*' }
        $local | Should -Contain 'ErpApi__BaseAddress'
        $local | Should -Contain 'LocalUser__Roles__0'
        $local | Should -Contain 'ErpApi__LocalTokens__ClientId'
        foreach ($name in $local) { $env.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
    }

    It 'calls the ERP API by its Service name, as itself: its own token, signed with the key of the install' {
        $env['ErpApi__BaseAddress'] | Should -Be 'http://erp-api:8080'
        $env['ErpApi__LocalTokens__Issuer'] | Should -Be 'https://norvia.local/tokens'
        $env['ErpApi__LocalTokens__Audience'] | Should -Be 'api://norvia-erp'
        $env['ErpApi__LocalTokens__ClientId'] | Should -Be ([regex]::Match($localTokensSource, 'public const string ErpUiClientId = "([^"]+)"').Groups[1].Value)
        $env['ErpApi__LocalTokens__SigningKey'] | Should -Be 'secret:local-tokens/signing-key'
    }

    It 'signs Finance in locally, holding the Finance app role' {
        $env['LocalUser__Name'] | Should -Not -BeNullOrEmpty
        $env['LocalUser__Roles__0'] | Should -Be 'Finance'
    }

    It 'sends to the second Service Bus emulator, which holds the ERP''s topics, as the AppHost''s messaging reference does' {
        $env['ConnectionStrings__messaging'] | Should -Match '^Endpoint=sb://messaging-2;'
    }

    It 'reads the seed file the image carries' {
        $env['Seed__Path'] | Should -Be 'seed/norvia.seed.json'
    }

    It 'is not given the settings of Azure: no Entra scope, no managed identity, no Foundry, no Agents' {
        $env.Keys | Where-Object { $_ -match 'Entra|Foundry|ManagedIdentity|APPLICATIONINSIGHTS|^Agents__' } | Should -BeNullOrEmpty
    }
}

Describe 'the ERP in the rest of the chart' -Skip:$script:helmMissing {
    It 'is let through by the network policy from the ingress controller, as the other exposed applications are' {
        $policy = (Get-Doc $docs 'NetworkPolicy' 'allow-ingress-controller').Text
        $policy | Should -Match '- erp-api'
        $policy | Should -Match '- erp-web'
    }

    It 'is called by the workers at the Service name erp-api, which is the Service' {
        (Get-Doc $docs 'Service' 'erp-api') | Should -Not -BeNullOrEmpty
        (Get-Env (Get-Doc $docs 'Deployment' 'billing-worker'))['Billing__ErpBaseAddress'] | Should -Be 'http://erp-api:8080'
    }
}
