#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's Partner portal and Simulator (#1021): partner-portal-web and simulator-web as Deployments and Services behind the internal-only
# ingress, each waiting for its own migrations Job. The seam is the output of `helm template`, so nothing here needs a cluster. The settings the AppHost gives the
# two applications are compared with the AppHost's source: the chart gives them the same names, apart from the ones it replaces on purpose (connection strings,
# the links a browser follows, LocalAuth:Allowed, ADR 0053).
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

    # The Secrets a Deployment takes whole, as envFrom.
    function script:Get-EnvFrom($Doc) {
        @([regex]::Matches($Doc.Text, '(?m)^\s+- secretRef:\s*\n\s+name: (\S+)') | ForEach-Object { $_.Groups[1].Value })
    }

    # Every setting the application sees: its own environment and the keys of the Secrets it takes whole.
    function script:Get-Available($Docs, $Doc) {
        $names = [System.Collections.Generic.List[string]]::new()
        $names.AddRange([string[]] @((Get-Env $Doc).Keys))
        foreach ($secret in Get-EnvFrom $Doc) {
            $data = ((Get-Doc $Docs 'Secret' $secret).Text -split '(?m)^data:')[1]
            foreach ($m in [regex]::Matches($data, '(?m)^\s+(\S+): \S+\s*$')) { $names.Add($m.Groups[1].Value) }
        }
        $names
    }

    # The names of the settings the AppHost gives the application when it runs locally: every `WithEnvironment("Name", value)` of a statement that starts with the
    # application's variable (the declaration, or `variable.WithEnvironment(...)`), except the ones whose value comes from the estate (Azure's, in publish mode).
    function script:Get-AppHostSettings([string] $Variable) {
        $statements = [regex]::Matches($script:appHost, "(?ms)^[ ]*(?:var )?$Variable\b.*?;[ ]*\r?$") | ForEach-Object Value
        $statements | ForEach-Object { [regex]::Matches($_, 'WithEnvironment\("([A-Za-z0-9_]+)",(?>\s*)(?!estate\.|ReferenceExpression|partnerPortalSecrets)') } | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -notlike 'Agents__*' } | Select-Object -Unique
    }

    function script:Get-Constant([string] $Name) {
        [regex]::Match($script:localTokensSource, "public const string $Name = ""([^""]+)""").Groups[1].Value
    }

    $script:docs = Get-Rendered
    $script:appHost = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/AppHost.cs') -Raw
    $script:localTokensSource = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/LocalTokens.cs') -Raw
    $script:seed = Get-Content (Join-Path $script:root 'seed/norvia.seed.json') -Raw | ConvertFrom-Json
}

Describe 'the Partner portal and the Simulator' -Skip:$script:helmMissing {
    It '<_> is one replica with a Service on the app port, probed on /health and /alive, and pulls its image from the configured registry' -ForEach 'partner-portal-web', 'simulator-web' {
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

    It '<App> waits for the <Database> migrations Job, and only that one, as the AppHost''s WaitForCompletion does' -ForEach @(
        @{ App = 'partner-portal-web'; Database = 'partner-portal' }
        @{ App = 'simulator-web'; Database = 'simulator' }
    ) {
        $text = (Get-Doc $docs 'Deployment' $App).Text
        $text | Should -Match 'serviceAccountName: migrations-waiter'
        $text | Should -Match 'norvia\.io/waits-for-migrations: "true"'
        $text | Should -Match "(?s)initContainers:.*name: wait-for-$Database-migrations.*- job/$Database-migrations-1\b"
        ([regex]::Matches($text, 'name: wait-for-[\w-]+-migrations')).Count | Should -Be 1
    }

    It '<_> is reached through the ingress at its own hostname, which only the LAN or VPN may use' -ForEach 'partner-portal-web', 'simulator-web' {
        $ingress = Get-Doc $docs 'Ingress' $_
        $ingress | Should -Not -BeNullOrEmpty
        $ingress.Text | Should -Match "host: ${_}\.norvia\.test"
        $ingress.Text | Should -Match ('(?s)name: ' + $_ + '\s+port:\s+name: http')
        $ingress.Text | Should -Match 'router\.middlewares: norvia-internal-only@kubernetescrd'
    }

    It '<_> sends its telemetry to the dashboard' -ForEach 'partner-portal-web', 'simulator-web' {
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $env['OTEL_EXPORTER_OTLP_ENDPOINT'] | Should -Be 'http://aspire-dashboard:18889'
        $env['OTEL_SERVICE_NAME'] | Should -Be $_
    }

    It '<App> reads the <Database> database as the sa login, from the Secret the migrations use' -ForEach @(
        @{ App = 'partner-portal-web'; Database = 'partner-portal' }
        @{ App = 'simulator-web'; Database = 'simulator' }
    ) {
        $env = Get-Env (Get-Doc $docs 'Deployment' $App)
        $env['MSSQL_SA_PASSWORD'] | Should -Be 'secret:sql/sa-password'
        $env["ConnectionStrings__$Database"] | Should -Be "Server=sql,1433;Database=$Database;User ID=sa;Password=`$(MSSQL_SA_PASSWORD);TrustServerCertificate=true"
        @($env.Keys).IndexOf('MSSQL_SA_PASSWORD') | Should -BeLessThan @($env.Keys).IndexOf("ConnectionStrings__$Database") -Because 'Kubernetes expands only variables defined above'
    }

    It '<_> reads the seed file the image carries' -ForEach 'partner-portal-web', 'simulator-web' {
        (Get-Env (Get-Doc $docs 'Deployment' $_))['Seed__Path'] | Should -Be 'seed/norvia.seed.json'
    }

    It '<_> is not given the settings of Azure: no Entra, no Foundry, no Application Insights, no managed identity' -ForEach 'partner-portal-web', 'simulator-web' {
        (Get-Available $docs (Get-Doc $docs 'Deployment' $_)) | Where-Object { $_ -match 'Authority|Foundry|Entra|ManagedIdentity|APPLICATIONINSIGHTS|DriverSignIns|Faults__LogicApp__(Name|ResourceGroupId)' } | Should -BeNullOrEmpty
    }

    It '<_> takes each Secret it reads whole once, so the pod has one envFrom' -ForEach 'partner-portal-web', 'simulator-web' {
        ([regex]::Matches((Get-Doc $docs 'Deployment' $_).Text, '(?m)^\s+envFrom:')).Count | Should -BeLessOrEqual 1
    }
}

Describe 'partner-portal-web' -Skip:$script:helmMissing {
    BeforeAll {
        $script:deployment = Get-Doc $script:docs 'Deployment' 'partner-portal-web'
        $script:env = Get-Env $script:deployment
    }

    It 'has every setting the AppHost gives it locally, from its environment or the Secrets it takes whole' {
        $local = Get-AppHostSettings 'partnerPortal'
        $local | Should -Contain 'PartnerApi__LocalTokens__Issuer'
        $local | Should -Contain 'Faults__LocalTokens__Issuer'
        $local | Should -Contain 'ResetEndpoint__ApiKey'
        $available = Get-Available $docs $deployment
        foreach ($name in $local) { $available | Should -Contain $name -Because "the AppHost sets $name" }
    }

    It 'calls the Partner API by its Service name, as each Persona: its own tokens, signed with the key of the install' {
        $env['PartnerApi__BaseAddress'] | Should -Be 'http://partner-api:8080/partner/v1'
        $env['PartnerApi__LocalTokens__Issuer'] | Should -Be 'https://norvia.local/tokens'
        $env['PartnerApi__LocalTokens__Audience'] | Should -Be (Get-Constant 'PartnerApiAudience')
        $env['PartnerApi__LocalTokens__SigningKey'] | Should -Be 'secret:local-tokens/signing-key'
    }

    It 'knows the client ID of every Persona of the seed file, which stands in for its Entra client app' {
        $pairs = @($seed.personas | ForEach-Object { "$($_.id)=norvia-local-persona-$($_.id)" }) -join ';'
        $env['PartnerApi__PersonaClientIds'] | Should -Be $pairs
    }

    It 'gives each Persona the app roles the estate grants it on the Partner API' {
        $roles = @{
            Shipper = 'SubmitRole', 'TrackingReadRole', 'DraftRole'
            Carrier = 'RespondRole', 'TrackingSubmitRole', 'ProofsOfDeliverySubmitRole', 'CarrierInvoicesSubmitRole'
        }
        foreach ($persona in $seed.personas) {
            $expected = $roles[$persona.party.type]
            for ($i = 0; $i -lt $expected.Count; $i++) {
                $env["PartnerApi__LocalTokens__Roles__$($persona.id)__$i"] | Should -Be (Get-Constant $expected[$i]) -Because "$($persona.id) role $i"
            }
            @($env.Keys | Where-Object { $_ -like "PartnerApi__LocalTokens__Roles__$($persona.id)__*" }).Count | Should -Be $expected.Count
        }
    }

    It 'takes the Personas'' subscription keys and the Consignee tracking key from the Secret the Partner API reads, and its endpoints'' keys from endpoint-keys' {
        Get-EnvFrom $deployment | Should -Be @('subscription-keys', 'endpoint-keys')
        $available = Get-Available $docs $deployment
        foreach ($persona in $seed.personas) {
            $available | Should -Contain "PartnerApi__SubscriptionKeys__$($persona.id)"
            $available | Should -Contain "$($persona.party.type)Endpoint__ApiKeys__$($persona.party.id)"
        }
        $available | Should -Contain 'PartnerApi__ConsigneeSubscriptionKey'
    }

    It 'allows the Simulator''s Reset estate, with the key the Simulator holds' {
        $env['Reset__Enabled'] | Should -Be 'true'
        Get-Available $docs $deployment | Should -Contain 'ResetEndpoint__ApiKey'
    }

    It 'switches the faults of the TMS and the Driver app through their Services, as the presenter: its own token, signed with the key of the install' {
        $env['Faults__Owners__driver-app__Address'] | Should -Be 'http://driver-app-api:8080'
        $env['Faults__Owners__tms__Address'] | Should -Be 'http://tms-api:8080'
        $env['Faults__Owners__driver-app__Audience'] | Should -Be (Get-Constant 'DriverAppAudience')
        $env['Faults__Owners__tms__Audience'] | Should -Be (Get-Constant 'TmsAudience')
        $env['Faults__LocalTokens__Issuer'] | Should -Be 'https://norvia.local/tokens'
        $env['Faults__LocalTokens__ClientId'] | Should -Be (Get-Constant 'PartnerPortalClientId')
        $env['Faults__LocalTokens__SigningKey'] | Should -Be 'secret:local-tokens/signing-key'
    }

    It 'keeps the Driver reports Logic App''s flag in a file of its own: the local way, which needs no Azure' {
        $env['Faults__LogicApp__FlagPath'] | Should -Not -BeNullOrEmpty
    }

    It 'turns the Shipper tracking assistant on only with agents.enabled' {
        $env.Contains('Agents__Enabled') | Should -BeFalse
        (Get-Env (Get-Doc (Get-Rendered @('agents.enabled=true')) 'Deployment' 'partner-portal-web'))['Agents__Enabled'] | Should -Be 'true'
    }
}

Describe 'simulator-web' -Skip:$script:helmMissing {
    BeforeAll {
        $script:deployment = Get-Doc $script:docs 'Deployment' 'simulator-web'
        $script:env = Get-Env $script:deployment
    }

    It 'has every setting the AppHost gives it locally' {
        $local = Get-AppHostSettings 'simulator'
        $local | Should -Contain 'Simulator__Enabled'
        $local | Should -Contain 'PartnerPortal__ResetApiKey'
        $local | Should -Contain 'TmsApi__LocalTokens__ClientId'
        $available = Get-Available $docs $deployment
        foreach ($name in $local) { $available | Should -Contain $name -Because "the AppHost sets $name" }
    }

    It 'is on, with the caps the AppHost gives it locally' {
        $env['Simulator__Enabled'] | Should -Be 'true'
        $env['Simulator__MaxOrdersPerRun'] | Should -Be '500'
        $env['Simulator__MaxConcurrentRequests'] | Should -Be '4'
    }

    It 'signs the operator in with both roles, behind LocalAuth:Allowed' {
        $env['LocalOperator__Name'] | Should -Be 'operator (local)'
        $env['LocalOperator__Roles__0'] | Should -Be 'Simulator.Run'
        $env['LocalOperator__Roles__1'] | Should -Be 'Simulator.Read'
        $env['LocalAuth__Allowed'] | Should -Be 'true'
        (Get-Env (Get-Doc (Get-Rendered @('localAuth.allowed=false')) 'Deployment' 'simulator-web'))['LocalAuth__Allowed'] | Should -Be 'false'
    }

    It 'books through the Partner API as each Persona, with the roles the estate grants it and the subscription key of the install' {
        $env['PartnerApi__BaseAddress'] | Should -Be 'http://partner-api:8080/partner/v1'
        $env['PartnerApi__LocalTokens__Audience'] | Should -Be (Get-Constant 'PartnerApiAudience')
        $env['PartnerApi__LocalTokens__SigningKey'] | Should -Be 'secret:local-tokens/signing-key'
        $env['PartnerApi__PersonaClientIds'] | Should -Be (@($seed.personas | ForEach-Object { "$($_.id)=norvia-local-persona-$($_.id)" }) -join ';')
        $roles = @{ Shipper = , 'SubmitRole'; Carrier = 'RespondRole', 'TrackingSubmitRole', 'ProofsOfDeliverySubmitRole', 'CarrierInvoicesSubmitRole' }
        foreach ($persona in $seed.personas) {
            $env["PartnerApi__SubscriptionKeys__$($persona.id)"] | Should -Be "secret:subscription-keys/PartnerApi__SubscriptionKeys__$($persona.id)"
            $expected = $roles[$persona.party.type]
            for ($i = 0; $i -lt $expected.Count; $i++) {
                $env["PartnerApi__LocalTokens__Roles__$($persona.id)__$i"] | Should -Be (Get-Constant $expected[$i])
            }
            @($env.Keys | Where-Object { $_ -like "PartnerApi__LocalTokens__Roles__$($persona.id)__*" }).Count | Should -Be $expected.Count
        }
    }

    It 'signs its own tokens for the TMS API, the Driver app API and the ERP API as the Simulator, with the key of the install' {
        foreach ($api in @{ TmsApi = 'TmsAudience'; DriverApp = 'DriverAppAudience'; ErpApi = 'ErpAudience' }.GetEnumerator()) {
            $env["$($api.Key)__LocalTokens__Issuer"] | Should -Be 'https://norvia.local/tokens'
            $env["$($api.Key)__LocalTokens__Audience"] | Should -Be (Get-Constant $api.Value)
            $env["$($api.Key)__LocalTokens__ClientId"] | Should -Be (Get-Constant 'SimulatorClientId')
            $env["$($api.Key)__LocalTokens__SigningKey"] | Should -Be 'secret:local-tokens/signing-key'
        }
    }

    It 'calls the TMS API, the Driver app API, the ERP API and the Partner portal by their Service names' {
        $env['TmsApi__BaseAddress'] | Should -Be 'http://tms-api:8080'
        $env['DriverApp__BaseAddress'] | Should -Be 'http://driver-app-api:8080'
        $env['ErpApi__BaseAddress'] | Should -Be 'http://erp-api:8080'
        $env['PartnerPortal__BaseAddress'] | Should -Be 'http://partner-portal-web:8080'
    }

    It 'links the presenter''s browser to the TMS UI and the Integration console at their hostnames, which a browser can reach' {
        $env['TmsWeb__Address'] | Should -Be 'https://tms-web.norvia.test'
        $env['IntegrationConsole__Address'] | Should -Be 'https://integration-console-web.norvia.test'
        $plain = Get-Rendered @('edge.tls.enabled=false')
        (Get-Env (Get-Doc $plain 'Deployment' 'simulator-web'))['TmsWeb__Address'] | Should -Be 'http://tms-web.norvia.test'
    }

    It 'reads each Persona''s inbox and calls the portal''s reset with the keys the portal asks its callers for' {
        foreach ($persona in $seed.personas) {
            $env["PartnerPortal__$($persona.party.type)EndpointKeys__$($persona.party.id)"] | Should -Be "secret:endpoint-keys/$($persona.party.type)Endpoint__ApiKeys__$($persona.party.id)"
        }
        $env['PartnerPortal__ResetApiKey'] | Should -Be 'secret:endpoint-keys/ResetEndpoint__ApiKey'
    }

    It 'plays the personas agent only with agents.enabled' {
        $env.Contains('Agents__Enabled') | Should -BeFalse
        (Get-Env (Get-Doc (Get-Rendered @('agents.enabled=true')) 'Deployment' 'simulator-web'))['Agents__Enabled'] | Should -Be 'true'
    }
}

Describe 'the endpoint keys of the Partner portal''s reset' -Skip:$script:helmMissing {
    It 'is one key more in endpoint-keys, generated like the others and different from them' {
        $text = (Get-Doc $docs 'Secret' 'endpoint-keys').Text
        $data = @{}
        foreach ($m in [regex]::Matches(($text -split '(?m)^data:')[1], '(?m)^\s+(\S+): (\S+)\s*$')) {
            $data[$m.Groups[1].Value] = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($m.Groups[2].Value.Trim('"')))
        }
        $data['ResetEndpoint__ApiKey'] | Should -Match '^[a-z0-9]{32}$'
        ($data.Values | Select-Object -Unique).Count | Should -Be $data.Count
    }
}

Describe 'the Partner portal and the Simulator in the rest of the chart' -Skip:$script:helmMissing {
    It 'are let through by the network policy from the ingress controller, as the other exposed applications are' {
        $policy = (Get-Doc $docs 'NetworkPolicy' 'allow-ingress-controller').Text
        $policy | Should -Match '- partner-portal-web'
        $policy | Should -Match '- simulator-web'
    }

    It 'is called by the workers at the Service name partner-portal-web, which is the Service' {
        (Get-Doc $docs 'Service' 'partner-portal-web') | Should -Not -BeNullOrEmpty
        (Get-Env (Get-Doc $docs 'Deployment' 'booking-worker'))['Booking__Carriers__CAR-0001__Address'] | Should -Be 'http://partner-portal-web:8080/carrier/v1/CAR-0001/bookings'
    }

    It 'is where the Consignee''s tracking link points, at the portal''s hostname, in the TMS UI and in the email' {
        (Get-Env (Get-Doc $docs 'Deployment' 'tms-web'))['PartnerPortal__BaseAddress'] | Should -Be 'https://partner-portal-web.norvia.test'
        (Get-Env (Get-Doc $docs 'Deployment' 'consignee-notification-worker'))['ConsigneeNotification__PartnerPortalBaseAddress'] | Should -Be 'https://partner-portal-web.norvia.test'
    }
}
