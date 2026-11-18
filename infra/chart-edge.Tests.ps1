#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's edge (#928): the Partner API and agent MCP stand-ins, the keys generated per install, and the ingress
# that gives every application its own hostname. The seam is the output of `helm template`, so nothing here needs a cluster. What the
# chart repeats from the AppHost (the token constants, the Personas, the MCP stand-in's settings) is compared with the AppHost's source.
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

    # The Secret's keys, each with its value as it is stored (base64 decoded).
    function script:Get-SecretData($Doc) {
        $data = [ordered]@{}
        foreach ($m in [regex]::Matches(($Doc.Text -split '(?m)^data:')[1], '(?m)^\s+(\S+): (\S+)\s*$')) {
            $data[$m.Groups[1].Value] = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($m.Groups[2].Value.Trim('"')))
        }
        $data
    }

    $script:docs = Get-Rendered
    $script:withAgents = Get-Rendered @('agents.enabled=true')
    $script:appHost = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/AppHost.cs') -Raw
    $script:localTokensSource = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/LocalTokens.cs') -Raw
    $script:values = Get-Content (Join-Path $script:chart 'values.yaml') -Raw
    $script:seed = Get-Content (Join-Path $script:root 'seed/norvia.seed.json') -Raw | ConvertFrom-Json
}

Describe 'the Partner API stand-in' -Skip:$script:helmMissing {
    BeforeAll {
        $script:partnerApi = Get-Doc $script:docs 'Deployment' 'partner-api'
        $script:partnerApiEnv = Get-Env $script:partnerApi
    }

    It 'is one Deployment with a Service, probed on /health and /alive' {
        $partnerApi | Should -Not -BeNullOrEmpty
        $partnerApi.Text | Should -Match '(?m)^\s+replicas: 1$'
        $partnerApi.Text | Should -Match '(?s)readinessProbe:\s+httpGet:\s+path: /health'
        $partnerApi.Text | Should -Match '(?s)livenessProbe:\s+httpGet:\s+path: /alive'
        (Get-Doc $docs 'Service' 'partner-api').Text | Should -Match 'port: 8080'
    }

    It 'pulls its image from the configured registry' {
        $partnerApi.Text | Should -Match 'image: partner-api:latest'
        $rendered = Get-Rendered @('image.registry=registry.example.test/norvia', 'image.tag=1.2.3')
        (Get-Doc $rendered 'Deployment' 'partner-api').Text | Should -Match 'image: registry\.example\.test/norvia/partner-api:1\.2\.3'
    }

    It 'reads the Service Bus emulator that holds the Carrier tracking path, Azurite, and the TMS API' {
        $partnerApiEnv['ConnectionStrings__messaging'] | Should -Match '^Endpoint=sb://messaging-2;'
        $partnerApiEnv['ConnectionStrings__blobs'] | Should -Match 'BlobEndpoint=http://azurite:10000/devstoreaccount1;'
        $partnerApiEnv['Tms__BaseAddress'] | Should -Be 'http://tms-api:8080'
        $partnerApiEnv['CarrierInvoice__BaseAddress'] | Should -Be 'http://carrier-invoice-worker:8080'
    }

    It 'takes the issuer, audiences and client IDs the AppHost gives it' {
        $expected = [ordered]@{
            PartnerApi__Tokens__Issuer = 'Issuer'; PartnerApi__Tokens__Audience = 'PartnerApiAudience'
            Tms__Tokens__Issuer = 'Issuer'; Tms__Tokens__Audience = 'TmsAudience'; Tms__Tokens__ClientId = 'IntakeLogicAppClientId'
            Tms__AnswerClientId = 'AnswerLogicAppClientId'; Tms__ShipperTrackingClientId = 'ApiManagementClientId'
            CarrierInvoice__Tokens__Issuer = 'Issuer'; CarrierInvoice__Tokens__Audience = 'CarrierInvoiceTriggerAudience'; CarrierInvoice__Tokens__ClientId = 'ApiManagementClientId'
        }
        foreach ($name in $expected.Keys) {
            $constant = [regex]::Match($localTokensSource, "public const string $($expected[$name]) = ""([^""]+)""").Groups[1].Value
            $constant | Should -Not -BeNullOrEmpty
            $partnerApiEnv[$name] | Should -Be $constant -Because $name
        }
    }

    It 'signs and checks tokens with the install''s key, from a Secret' {
        foreach ($name in 'PartnerApi__Tokens__SigningKey', 'Tms__Tokens__SigningKey', 'CarrierInvoice__Tokens__SigningKey') {
            $partnerApiEnv[$name] | Should -Be 'secret:local-tokens/signing-key' -Because $name
        }
    }

    It 'takes every Persona''s subscription key from the Secret the other callers share, and no key in the Deployment' {
        $partnerApi.Text | Should -Match '(?s)envFrom:\s+- secretRef:\s+name: subscription-keys'
        $partnerApi.Text | Should -Not -Match 'SubscriptionKeys__'
    }

    It 'maps each Persona''s client to its Shipper or Carrier' {
        foreach ($persona in $seed.personas) {
            $map = if ($persona.party.type -eq 'Shipper') { 'ShippersByClientId' } else { 'CarriersByClientId' }
            $partnerApiEnv["PartnerApi__${map}__norvia-local-persona-$($persona.id)"] | Should -Be $persona.party.id
        }
    }

    It 'keeps the Intake agent off unless the agents are on' {
        $partnerApiEnv.Contains('Agents__Enabled') | Should -BeFalse
        (Get-Env (Get-Doc $withAgents 'Deployment' 'partner-api'))['Agents__Enabled'] | Should -Be 'true'
    }
}

Describe 'the values the chart repeats from the AppHost' -Skip:$script:helmMissing {
    It 'lists the seed file''s Personas, in its order' {
        $listed = [regex]::Matches($values, '(?m)^\s+- \{ id: (\S+), type: (\S+), party: (\S+) \}') | ForEach-Object { "$($_.Groups[1].Value) $($_.Groups[2].Value) $($_.Groups[3].Value)" }
        $listed | Should -Be ($seed.personas | ForEach-Object { "$($_.id) $($_.party.type) $($_.party.id)" })
    }

    It 'holds each local token constant as LocalTokens.cs has it' {
        $block = [regex]::Match($values, '(?ms)^localTokens:\n(.*?)(?=^\S|\z)').Groups[1].Value
        $pairs = [regex]::Matches($block, '(?m)^\s+(\w+): (\S+)$')
        $pairs.Count | Should -BeGreaterThan 10
        foreach ($pair in $pairs) {
            $constant = [regex]::Match($localTokensSource, "public const string $($pair.Groups[1].Value) = ""([^""]+)""").Groups[1].Value
            $pair.Groups[2].Value | Should -Be $constant -Because $pair.Groups[1].Value
        }
    }
}

Describe 'the keys generated per install' -Skip:$script:helmMissing {
    BeforeAll {
        $script:subscriptionKeys = Get-SecretData (Get-Doc $script:docs 'Secret' 'subscription-keys')
        $script:signing = Get-SecretData (Get-Doc $script:docs 'Secret' 'local-tokens')
    }

    It 'has a subscription key for each Persona and one for the Consignee tracking product, all different, as env var names the callers can read' {
        $names = @('PartnerApi__ConsigneeSubscriptionKey') + @($seed.personas | ForEach-Object { "PartnerApi__SubscriptionKeys__$($_.id)" })
        $subscriptionKeys.Keys | Should -Be $names
        ($subscriptionKeys.Values | Select-Object -Unique).Count | Should -Be $names.Count
        $subscriptionKeys.Values | ForEach-Object { $_ | Should -Match '^[a-z0-9]{32}$' }
    }

    It 'has a token signing key that is base64 and at least 256 bits, as JWT bearer configuration takes it' {
        $bytes = [Convert]::FromBase64String($signing['signing-key'])
        $bytes.Length | Should -BeGreaterOrEqual 32
    }

    It 'is generated at each render, not written in the chart' {
        $again = Get-SecretData (Get-Doc (Get-Rendered) 'Secret' 'subscription-keys')
        $again['PartnerApi__ConsigneeSubscriptionKey'] | Should -Not -Be $subscriptionKeys['PartnerApi__ConsigneeSubscriptionKey']
        $values | Should -Not -Match '(?m)^\s+signingKey:\s+\S'
    }
}

Describe 'the agent MCP stand-in' -Skip:$script:helmMissing {
    It 'is not deployed unless the agents are on, as under Aspire' {
        Get-Doc $docs 'Deployment' 'agent-mcp' | Should -BeNullOrEmpty
        Get-Doc $docs 'Service' 'agent-mcp' | Should -BeNullOrEmpty
    }

    Context 'with the agents on' {
        BeforeAll {
            $script:mcp = Get-Doc $script:withAgents 'Deployment' 'agent-mcp'
            $script:mcpEnv = Get-Env $script:mcp
        }

        It 'is one Deployment with a Service, probed on /health and /alive' {
            $mcp | Should -Not -BeNullOrEmpty
            $mcp.Text | Should -Match 'image: agent-mcp:latest'
            $mcp.Text | Should -Match '(?s)readinessProbe:\s+httpGet:\s+path: /health'
            (Get-Doc $withAgents 'Service' 'agent-mcp').Text | Should -Match 'port: 8080'
        }

        It 'has every setting the AppHost gives it, with the same value for each token constant, and the key of the install for each token' {
            $block = [regex]::Match($appHost, 'AddProject<Projects\.Norvia_Integration_LocalAgentMcp>[^;]*;').Value
            $block | Should -Not -BeNullOrEmpty
            $names = [regex]::Matches($block, 'WithEnvironment\("((?:AgentMcp|Agents)__[A-Za-z_]+)"') | ForEach-Object { $_.Groups[1].Value }
            $names.Count | Should -BeGreaterThan 20
            foreach ($name in $names) { $mcpEnv.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
            foreach ($name in $mcpEnv.Keys | Where-Object { $_ -match '__SigningKey$' }) { $mcpEnv[$name] | Should -Be 'secret:local-tokens/signing-key' }
            ($mcpEnv.Keys | Where-Object { $_ -match '^AgentMcp__.*(Tokens)__Audience$' }).Count | Should -BeGreaterThan 8
        }

        It 'calls the applications'' agent views by their Service names' {
            $mcpEnv['AgentMcp__ConsoleAgentApi'] | Should -Be 'http://integration-console-web:8080/agent/v1'
            $mcpEnv['AgentMcp__HealthCheck__Api'] | Should -Be 'http://health-check-web:8080/agent/v1'
            $mcpEnv['AgentMcp__CustomerService__Api'] | Should -Be 'http://tms-api:8080/v1/agent/customer-service'
            $mcpEnv['AgentMcp__FinanceMatching__Api'] | Should -Be 'http://erp-api:8080/v1/agent/finance'
        }
    }
}

Describe 'the ingress' -Skip:$script:helmMissing {
    It '<_> gets its own hostname under the base domain, on the ingress class' -ForEach 'partner-api' {
        $ingress = Get-Doc $docs 'Ingress' $_
        $ingress | Should -Not -BeNullOrEmpty
        $ingress.Text | Should -Match 'ingressClassName: traefik'
        $ingress.Text | Should -Match "host: $_\.norvia\.test"
        $ingress.Text | Should -Match '(?s)name: partner-api\s+port:\s+name: http'
    }

    It 'routes the MCP stand-in only when it is deployed' {
        Get-Doc $docs 'Ingress' 'agent-mcp' | Should -BeNullOrEmpty
        (Get-Doc $withAgents 'Ingress' 'agent-mcp').Text | Should -Match 'host: agent-mcp\.norvia\.test'
    }

    It 'asks cert-manager for a certificate from the issuer in the values, one Secret per hostname' {
        $rendered = Get-Rendered @('edge.tls.clusterIssuer=letsencrypt-staging')
        $text = (Get-Doc $rendered 'Ingress' 'partner-api').Text
        $text | Should -Match 'cert-manager\.io/cluster-issuer: letsencrypt-staging'
        $text | Should -Match '(?s)tls:\s+- hosts:\s+- partner-api\.norvia\.test\s+secretName: partner-api-tls'
    }

    It 'names no issuer and asks for none when none is set, and leaves out TLS when it is off' {
        (Get-Doc $docs 'Ingress' 'partner-api').Text | Should -Not -Match 'cert-manager\.io/cluster-issuer'
        $off = Get-Rendered @('edge.tls.enabled=false')
        (Get-Doc $off 'Ingress' 'partner-api').Text | Should -Not -Match 'tls:'
    }

    It 'takes another ingress class, a base domain and annotations from the values' {
        $rendered = Get-Rendered @('edge.ingress.className=nginx', 'edge.baseDomain=sandbox.example.org', 'edge.ingress.annotations.example\.org/flag=on')
        $text = (Get-Doc $rendered 'Ingress' 'partner-api').Text
        $text | Should -Match 'ingressClassName: nginx'
        $text | Should -Match 'host: partner-api\.sandbox\.example\.org'
        $text | Should -Match 'example\.org/flag: "?on"?'
    }

    It 'refuses to render without a base domain, since no default may point at one cluster' {
        $output = & helm template norvia $script:chart --set sql.saPassword=Test-Passw0rd 2>&1
        $LASTEXITCODE | Should -Not -Be 0
        ($output -join "`n") | Should -Match 'edge\.baseDomain'
    }
}

Describe 'the rendered chart' -Skip:$script:helmMissing {
    It 'passes helm lint with the agents on' {
        $output = & helm lint $script:chart --set sql.saPassword=Test-Passw0rd --set edge.baseDomain=norvia.test --set agents.enabled=true 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output -join "`n")
    }
}
