#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's telemetry phase 1 (#931, ADR 0027): the standalone Aspire dashboard, the OTLP settings of every
# application and the settings that point the Integration console at the dashboard. The seam is the output of `helm template`, so nothing
# here needs a cluster; the smoke script covers the running dashboard.
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

    # The value of one environment variable of a Deployment, or $null.
    function script:Get-EnvValue($Doc, [string] $Name) {
        $m = [regex]::Match($Doc.Text, "(?m)^\s+- name: $([regex]::Escape($Name))\n\s+value: ""?([^""\n]*)""?\s*$")
        if ($m.Success) { $m.Groups[1].Value }
    }

    $script:docs = Get-Rendered
    $script:dashboard = Get-Doc $script:docs 'Deployment' 'aspire-dashboard'
    $script:appHostSource = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/AppHost.cs') -Raw
    $script:helpers = Get-Content (Join-Path $script:chart 'templates/_helpers.tpl') -Raw
}

Describe 'the dashboard' -Skip:$script:helmMissing {
    It 'is one replica of the Aspire dashboard image, with no volume, so history does not survive a restart' {
        $dashboard | Should -Not -BeNullOrEmpty
        $dashboard.Text | Should -Match '(?m)^\s+replicas: 1$'
        $dashboard.Text | Should -Match 'image: mcr\.microsoft\.com/dotnet/aspire-dashboard:\d+\.\d+'
        $dashboard.Text | Should -Not -Match 'volumeMounts|persistentVolumeClaim'
    }

    It 'uses the major.minor of the Aspire packages the AppHost is built with' {
        $packages = Get-Content (Join-Path $root 'Directory.Packages.props') -Raw
        $aspire = [regex]::Match($packages, 'Include="Aspire\.Hosting\.Testing" Version="(\d+\.\d+)').Groups[1].Value
        $aspire | Should -Not -BeNullOrEmpty
        $dashboard.Text | Should -Match "aspire-dashboard:$([regex]::Escape($aspire))"
    }

    It 'serves the UI and telemetry API, OTLP over gRPC and OTLP over HTTP on a Service' {
        $service = Get-Doc $docs 'Service' 'aspire-dashboard'
        $service | Should -Not -BeNullOrEmpty
        foreach ($port in 18888, 18889, 18890) { $service.Text | Should -Match "port: $port" }
        $moved = Get-Rendered @('telemetry.dashboard.ports.ui=28888', 'telemetry.dashboard.ports.otlpGrpc=28889', 'telemetry.dashboard.ports.otlpHttp=28890')
        $movedDashboard = Get-Doc $moved 'Deployment' 'aspire-dashboard'
        (Get-EnvValue $movedDashboard 'DASHBOARD__OTLP__GRPCENDPOINTURL') | Should -Be 'http://+:28889'
        (Get-EnvValue $movedDashboard 'ASPNETCORE_URLS') | Should -Be 'http://+:28888'
        (Get-EnvValue $dashboard 'DASHBOARD__OTLP__GRPCENDPOINTURL') | Should -Be 'http://+:18889'
        (Get-EnvValue $dashboard 'DASHBOARD__OTLP__HTTPENDPOINTURL') | Should -Be 'http://+:18890'
        (Get-EnvValue $dashboard 'ASPNETCORE_URLS') | Should -Be 'http://+:18888'
    }

    It 'takes OTLP without a key but the telemetry API only with the generated one' {
        (Get-EnvValue $dashboard 'DASHBOARD__OTLP__AUTHMODE') | Should -Be 'Unsecured'
        (Get-EnvValue $dashboard 'DASHBOARD__API__ENABLED') | Should -Be 'true'
        (Get-EnvValue $dashboard 'DASHBOARD__API__AUTHMODE') | Should -Be 'ApiKey'
        $dashboard.Text | Should -Match '(?s)DASHBOARD__API__PRIMARYAPIKEY\s+valueFrom:\s+secretKeyRef:\s+name: dashboard-api-key\s+key: api-key'
    }

    It 'has an API key in a Secret, generated, and not in values' {
        $secret = Get-Doc $docs 'Secret' 'dashboard-api-key'
        $key = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([regex]::Match($secret.Text, '(?m)^\s+api-key: "?(\S+?)"?\s*$').Groups[1].Value))
        $key.Length | Should -BeGreaterOrEqual 32
        (Get-Content (Join-Path $chart 'values.yaml') -Raw) | Should -Not -Match 'apiKey|api-key'
        $other = Get-Rendered
        $otherKey = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([regex]::Match((Get-Doc $other 'Secret' 'dashboard-api-key').Text, '(?m)^\s+api-key: "?(\S+?)"?\s*$').Groups[1].Value))
        $otherKey | Should -Not -Be $key
    }

    It 'has no Ingress: the UI is reached with a port-forward' {
        Get-Doc $docs 'Ingress' 'aspire-dashboard' | Should -BeNullOrEmpty
    }

    It 'keeps the log limit as a value, as the dashboard calls it' {
        (Get-EnvValue $dashboard 'DASHBOARD__TELEMETRYLIMITS__MAXLOGCOUNT') | Should -Be '10000'
        $rendered = Get-Rendered @('telemetry.dashboard.maxLogCount=500')
        (Get-EnvValue (Get-Doc $rendered 'Deployment' 'aspire-dashboard') 'DASHBOARD__TELEMETRYLIMITS__MAXLOGCOUNT') | Should -Be '500'
    }
}

Describe 'every application' -Skip:$script:helmMissing {
    It 'sends OTLP to the dashboard, as its own service' {
        $apps = $docs | Where-Object { $_.Kind -eq 'Deployment' -and $_.Name -notin 'aspire-dashboard', 'messaging', 'messaging-2' }
        $apps.Count | Should -BeGreaterThan 10
        foreach ($app in $apps) {
            (Get-EnvValue $app 'OTEL_EXPORTER_OTLP_ENDPOINT') | Should -Be 'http://aspire-dashboard:18889' -Because $app.Name
            (Get-EnvValue $app 'OTEL_SERVICE_NAME') | Should -Be $app.Name -Because $app.Name
        }
    }

    It 'includes the Deployments only the Agents switch on' {
        $rendered = Get-Rendered @('agents.enabled=true')
        (Get-EnvValue (Get-Doc $rendered 'Deployment' 'agent-mcp') 'OTEL_EXPORTER_OTLP_ENDPOINT') | Should -Be 'http://aspire-dashboard:18889'
    }

    It 'speaks the protocol ServiceDefaults exports by default: OTLP over gRPC, on the port the dashboard serves it on' {
        $extensions = Get-Content (Join-Path $root 'src/Norvia.ServiceDefaults/Extensions.cs') -Raw
        $extensions | Should -Match 'OTEL_EXPORTER_OTLP_ENDPOINT'
        $extensions | Should -Not -Match 'OTEL_EXPORTER_OTLP_PROTOCOL'
        (Get-EnvValue $dashboard 'DASHBOARD__OTLP__GRPCENDPOINTURL') | Should -Match ':18889$'
    }
}

Describe 'the Integration console''s settings' -Skip:$script:helmMissing {
    It 'name the same keys as the AppHost gives the console' {
        $helpers | Should -Match 'LocalTelemetry__DashboardUrl'
        $helpers | Should -Match 'LocalTelemetry__ApiKey'
        $appHostSource | Should -Match 'WithEnvironment\("LocalTelemetry__DashboardUrl"'
        $appHostSource | Should -Match 'WithEnvironment\("LocalTelemetry__ApiKey"'
    }

    It 'point at the dashboard''s UI and API port and read its key from the Secret the dashboard checks' {
        $block = [regex]::Match($helpers, '(?s)define "norvia\.localTelemetryEnv".*?\{\{- end \}\}').Value
        $block | Should -Match 'value: http://aspire-dashboard:\{\{ \.Values\.telemetry\.dashboard\.ports\.ui \}\}'
        $block | Should -Match '(?s)secretKeyRef:\s+name: dashboard-api-key\s+key: api-key'
    }
}
