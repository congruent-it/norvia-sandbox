#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's Driver app (#1019): driver-app-api and driver-app-web as Deployments and Services behind the internal-only ingress, each waiting for the
# `driver-app` migrations Job. The seam is the output of `helm template`, so nothing here needs a cluster. The settings the AppHost gives the two applications
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

Describe 'the Driver app applications' -Skip:$script:helmMissing {
    It '<_> is one replica with a Service on the app port, probed on /health and /alive, and pulls its image from the configured registry' -ForEach 'driver-app-api', 'driver-app-web' {
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

    It '<_> waits for the driver-app migrations Job, and only that one, as the AppHost''s WaitForCompletion does' -ForEach 'driver-app-api', 'driver-app-web' {
        $text = (Get-Doc $docs 'Deployment' $_).Text
        $text | Should -Match 'serviceAccountName: migrations-waiter'
        $text | Should -Match 'norvia\.io/waits-for-migrations: "true"'
        $text | Should -Match '(?s)initContainers:.*name: wait-for-driver-app-migrations.*- job/driver-app-migrations-1\b'
        $text | Should -Not -Match 'wait-for-(?!driver-app-)[\w-]+-migrations'
    }

    It '<_> is reached through the ingress at its own hostname, which only the LAN or VPN may use' -ForEach 'driver-app-api', 'driver-app-web' {
        $ingress = Get-Doc $docs 'Ingress' $_
        $ingress | Should -Not -BeNullOrEmpty
        $ingress.Text | Should -Match "host: ${_}\.norvia\.test"
        $ingress.Text | Should -Match ('(?s)name: ' + $_ + '\s+port:\s+name: http')
        $ingress.Text | Should -Match 'router\.middlewares: norvia-internal-only@kubernetescrd'
    }

    It '<_> sends its telemetry to the dashboard' -ForEach 'driver-app-api', 'driver-app-web' {
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $env['OTEL_EXPORTER_OTLP_ENDPOINT'] | Should -Be 'http://aspire-dashboard:18889'
        $env['OTEL_SERVICE_NAME'] | Should -Be $_
    }

    It '<_> reads the driver-app database as the sa login, from the Secret the migrations use' -ForEach 'driver-app-api', 'driver-app-web' {
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $env['MSSQL_SA_PASSWORD'] | Should -Be 'secret:sql/sa-password'
        $env['ConnectionStrings__driver-app'] | Should -Be 'Server=sql,1433;Database=driver-app;User ID=sa;Password=$(MSSQL_SA_PASSWORD);TrustServerCertificate=true'
        @($env.Keys).IndexOf('MSSQL_SA_PASSWORD') | Should -BeLessThan @($env.Keys).IndexOf('ConnectionStrings__driver-app') -Because 'Kubernetes expands only variables defined above'
    }

    It '<_> stores the files of a Proof of delivery in the Driver app''s container on Azurite, as the AppHost''s driver-app-blobs reference does' -ForEach 'driver-app-api', 'driver-app-web' {
        (Get-Env (Get-Doc $docs 'Deployment' $_))['ConnectionStrings__driver-app-blobs'] | Should -Match 'BlobEndpoint=http://azurite:10000/devstoreaccount1;'
    }

    It '<_> reads the seed file the image carries' -ForEach 'driver-app-api', 'driver-app-web' {
        (Get-Env (Get-Doc $docs 'Deployment' $_))['Seed__Path'] | Should -Be 'seed/norvia.seed.json'
    }

    It '<_> is allowed to run its local sign-in or trust the local key where there is no identity provider: LocalAuth:Allowed' -ForEach 'driver-app-api', 'driver-app-web' {
        (Get-Env (Get-Doc $docs 'Deployment' $_))['LocalAuth__Allowed'] | Should -Be 'true'
        (Get-Env (Get-Doc (Get-Rendered @('localAuth.allowed=false')) 'Deployment' $_))['LocalAuth__Allowed'] | Should -Be 'false'
    }

    It '<_> is not given the settings of Azure: no Entra, no Foundry, no Application Insights, no Agents' -ForEach 'driver-app-api', 'driver-app-web' {
        (Get-Env (Get-Doc $docs 'Deployment' $_)).Keys | Where-Object { $_ -match 'Authority|Foundry|Entra|ManagedIdentity|APPLICATIONINSIGHTS|^Agents__' } | Should -BeNullOrEmpty
    }
}

Describe 'driver-app-api' -Skip:$script:helmMissing {
    BeforeAll { $script:env = Get-Env (Get-Doc $script:docs 'Deployment' 'driver-app-api') }

    It 'has every setting the AppHost gives it locally' {
        $local = Get-AppHostSettings 'driverAppApi'
        $local | Should -Contain 'Reset__Enabled'
        $local | Should -Contain 'Reset__QuietSubscriptions__0'
        $local | Should -Contain 'DriverEndpoint__ApiKey'
        $local | Should -Contain 'Authentication__Schemes__Bearer__ValidAudiences__0'
        foreach ($name in $local) { $env.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
    }

    It 'trusts the local key: the tokens of the install, signed with the Secret local-tokens/signing-key' {
        $env['Authentication__Schemes__Bearer__ValidIssuer'] | Should -Be 'https://norvia.local/tokens'
        $env['Authentication__Schemes__Bearer__SigningKeys__0__Issuer'] | Should -Be 'https://norvia.local/tokens'
        $env['Authentication__Schemes__Bearer__ValidAudiences__0'] | Should -Be 'api://norvia-driver-app'
        $env['Authentication__Schemes__Bearer__SigningKeys__0__Value'] | Should -Be 'secret:local-tokens/signing-key'
    }

    It 'takes its audience as LocalTokens.cs has it' {
        [regex]::Match($localTokensSource, 'public const string DriverAppAudience = "([^"]+)"').Groups[1].Value | Should -BeIn $env.Values
    }

    It 'asks its caller for the key of the Secret endpoint-keys, the one the Trip dispatch worker sends' {
        $env['DriverEndpoint__ApiKey'] | Should -Be 'secret:endpoint-keys/DriverEndpoint__ApiKey'
        (Get-Env (Get-Doc $docs 'Deployment' 'trip-dispatch-worker'))['TripDispatch__DriverApp__ApiKey'] | Should -Be $env['DriverEndpoint__ApiKey']
    }

    It 'publishes what the Driver reports to the second Service Bus emulator, which holds the driver-app-events topic, as the AppHost''s messaging reference does' {
        $env['ConnectionStrings__messaging'] | Should -Match '^Endpoint=sb://messaging-2;'
    }

    It 'quiets the driver-reports subscription before a reset, and allows the reset' {
        $env['Reset__QuietSubscriptions__0'] | Should -Be 'driver-app-events/driver-reports'
        @($env.Keys | Where-Object { $_ -like 'Reset__QuietSubscriptions__*' }).Count | Should -Be 1
        $env['Reset__Enabled'] | Should -Be 'true'
    }
}

Describe 'driver-app-web' -Skip:$script:helmMissing {
    BeforeAll { $script:env = Get-Env (Get-Doc $script:docs 'Deployment' 'driver-app-web') }

    It 'has every setting the AppHost gives it locally' {
        $local = Get-AppHostSettings 'driverAppWeb' | Where-Object { $_ -notlike 'DriverPicker__*' }  # Azure's other way to pick a Driver
        $local | Should -Contain 'LocalSignIn__Enabled'
        foreach ($name in $local) { $env.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
    }

    It 'signs the Drivers in with the sign-in stand-in, behind LocalAuth:Allowed' {
        $env['LocalSignIn__Enabled'] | Should -Be 'true'
        $env['LocalAuth__Allowed'] | Should -Be 'true'
    }

    It 'is not given the Driver app API''s credentials or the local key, which it does not use' {
        $env.Keys | Where-Object { $_ -match '^Authentication__|SigningKey|DriverEndpoint' } | Should -BeNullOrEmpty
    }
}

Describe 'the Driver app in the rest of the chart' -Skip:$script:helmMissing {
    It 'is let through by the network policy from the ingress controller, as the other exposed applications are' {
        $policy = (Get-Doc $docs 'NetworkPolicy' 'allow-ingress-controller').Text
        $policy | Should -Match '- driver-app-api'
        $policy | Should -Match '- driver-app-web'
    }

    It 'is called by the workers at the Service name driver-app-api, which is the Service' {
        (Get-Doc $docs 'Service' 'driver-app-api') | Should -Not -BeNullOrEmpty
        (Get-Env (Get-Doc $docs 'Deployment' 'driver-reports-worker'))['DriverReports__DriverApp__BaseAddress'] | Should -Be 'http://driver-app-api:8080'
    }
}
