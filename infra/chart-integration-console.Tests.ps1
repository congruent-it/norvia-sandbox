#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's Integration console and Estate health check (#1020): integration-console-web and health-check-web as Deployments and Services behind the
# internal-only ingress. Neither has a database, so neither waits for a migrations Job. The seam is the output of `helm template`, so nothing here needs a cluster. The
# settings the AppHost gives the two applications are compared with the AppHost's source: the chart gives them the same names, apart from the ones it replaces on purpose
# (the Service Bus addresses, the dashboard's address, LocalAuth:Allowed, ADR 0053).
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


Describe 'the Integration console and the Estate health check' -Skip:$script:helmMissing {
    It '<_> is one replica with a Service on the app port, probed on /health and /alive, and pulls its image from the configured registry' -ForEach 'integration-console-web', 'health-check-web' {
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

    It '<_> has no database, so it waits for no migrations Job and mounts no API token' -ForEach 'integration-console-web', 'health-check-web' {
        $text = (Get-Doc $docs 'Deployment' $_).Text
        $text | Should -Not -Match 'initContainers'
        $text | Should -Not -Match 'migrations-waiter'
        $text | Should -Match 'automountServiceAccountToken: false'
        (Get-Env (Get-Doc $docs 'Deployment' $_)).Keys | Where-Object { $_ -like 'ConnectionStrings__*' } | Should -BeNullOrEmpty
    }

    It '<_> is reached through the ingress at its own hostname, which only the LAN or VPN may use' -ForEach 'integration-console-web', 'health-check-web' {
        $ingress = Get-Doc $docs 'Ingress' $_
        $ingress | Should -Not -BeNullOrEmpty
        $ingress.Text | Should -Match "host: ${_}\.norvia\.test"
        $ingress.Text | Should -Match ('(?s)name: ' + $_ + '\s+port:\s+name: http')
        $ingress.Text | Should -Match 'router\.middlewares: norvia-internal-only@kubernetescrd'
    }

    It '<_> sends its telemetry to the dashboard' -ForEach 'integration-console-web', 'health-check-web' {
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $env['OTEL_EXPORTER_OTLP_ENDPOINT'] | Should -Be 'http://aspire-dashboard:18889'
        $env['OTEL_SERVICE_NAME'] | Should -Be $_
    }

    It '<_> is allowed to run its local operator sign-in where there is no identity provider: LocalAuth:Allowed' -ForEach 'integration-console-web', 'health-check-web' {
        (Get-Env (Get-Doc $docs 'Deployment' $_))['LocalAuth__Allowed'] | Should -Be 'true'
        (Get-Env (Get-Doc (Get-Rendered @('localAuth.allowed=false')) 'Deployment' $_))['LocalAuth__Allowed'] | Should -Be 'false'
    }

    It '<_> is not given the settings of Azure: no Entra, no Foundry, no Application Insights, no Agents' -ForEach 'integration-console-web', 'health-check-web' {
        (Get-Env (Get-Doc $docs 'Deployment' $_)).Keys | Where-Object { $_ -match 'Authority|Foundry|Entra|ManagedIdentity|APPLICATIONINSIGHTS|^Agents__' } | Should -BeNullOrEmpty
    }
}

Describe 'integration-console-web' -Skip:$script:helmMissing {
    BeforeAll { $script:env = Get-Env (Get-Doc $script:docs 'Deployment' 'integration-console-web') }

    It 'has every setting the AppHost gives it locally' {
        $local = Get-AppHostSettings 'integrationConsole'
        $local | Should -Contain 'LocalOperator__Name'
        $local | Should -Contain 'LocalTelemetry__DashboardUrl'
        foreach ($name in $local) { $env.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
    }

    It 'is the local operator, holding both console roles' {
        $env['LocalOperator__Roles__0'] | Should -Be 'IntegrationConsole.Read'
        $env['LocalOperator__Roles__1'] | Should -Be 'IntegrationConsole.Resubmit'
        $env['LocalOperator__Name'] | Should -Not -BeNullOrEmpty
    }

    It 'manages both Service Bus emulators, under the namespace names the AppHost uses, through the management port' {
        $env['LocalServiceBus__Namespaces__messaging__ConnectionString'] | Should -Match '^Endpoint=sb://messaging;'
        $env['LocalServiceBus__Namespaces__messaging__ManagementConnectionString'] | Should -Match '^Endpoint=sb://messaging:5300;'
        $env['LocalServiceBus__Namespaces__messaging-edge__ConnectionString'] | Should -Match '^Endpoint=sb://messaging-2;'
        $env['LocalServiceBus__Namespaces__messaging-edge__ManagementConnectionString'] | Should -Match '^Endpoint=sb://messaging-2:5300;'
        $appHost | Should -Match 'LocalServiceBus__Namespaces__\{name\}__ManagementConnectionString'
        $appHost | Should -Match '\("messaging-edge", messagingEdge\)'
    }

    It 'searches the dashboard for trails with its telemetry API key, which it reads from the Secret' {
        $env['LocalTelemetry__DashboardUrl'] | Should -Be 'http://aspire-dashboard:18888'
        $env['LocalTelemetry__ApiKey'] | Should -Be 'secret:dashboard-api-key/api-key'
    }

    It 'has no estate: the workspace and resource group are empty, and the retention is the placeholder the AppHost uses' {
        $env['Estate__LogAnalyticsWorkspaceId'] | Should -BeIn @('', '""')
        $env['Estate__ResourceGroupId'] | Should -BeIn @('', '""')
        $env['Estate__IntegrationRetentionDays'] | Should -Be '90'
    }
}

Describe 'health-check-web' -Skip:$script:helmMissing {
    BeforeAll { $script:env = Get-Env (Get-Doc $script:docs 'Deployment' 'health-check-web') }

    It 'has every setting the AppHost gives it locally' {
        $local = Get-AppHostSettings 'healthCheck' | Where-Object { $_ -ne 'TmsApi__BaseAddress' }  # Azure's cost view: the AppHost sets it in publish mode only
        $local | Should -Contain 'Estate__Snapshot'
        $local | Should -Contain 'IntegrationConsole__Address'
        foreach ($name in $local) { $env.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
    }

    It 'is the local operator, holding the three health check roles' {
        $env['LocalOperator__Roles__0'] | Should -Be 'HealthCheck.Run'
        $env['LocalOperator__Roles__1'] | Should -Be 'HealthCheck.Read'
        $env['LocalOperator__Roles__2'] | Should -Be 'HealthCheck.Cost'
    }

    It 'reads the estate-as-found snapshot, as under Aspire' {
        $env['Estate__Snapshot'] | Should -Be 'as-found'
    }

    It 'links to the Integration console by its Service name' {
        $env['IntegrationConsole__Address'] | Should -Be 'http://integration-console-web:8080'
    }
}

Describe 'the console and the health check in the rest of the chart' -Skip:$script:helmMissing {
    It 'are let through by the network policy from the ingress controller, as the other exposed applications are' {
        $policy = (Get-Doc $docs 'NetworkPolicy' 'allow-ingress-controller').Text
        $policy | Should -Match '- integration-console-web'
        $policy | Should -Match '- health-check-web'
    }

    It 'is the Service the agent MCP stand-in calls' {
        (Get-Env (Get-Doc (Get-Rendered @('agents.enabled=true')) 'Deployment' 'agent-mcp'))['AgentMcp__ConsoleAgentApi'] | Should -Be 'http://integration-console-web:8080/agent/v1'
    }
}
