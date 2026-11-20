#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's Logic App workers (#929): the 14 Local*Worker hosts as Deployments, and the endpoint keys they and the Partner
# portal share. The seam is the output of `helm template`, so nothing here needs a cluster. What the chart repeats from the AppHost (each
# worker's settings, token constants, emulator and signing keys) is compared with the AppHost's source.
BeforeDiscovery {
    $script:helmMissing = -not (Get-Command helm -ErrorAction SilentlyContinue)
    $root = Split-Path $PSScriptRoot -Parent
    $script:workerNames = [regex]::Matches((Get-Content (Join-Path $root 'src/Norvia.AppHost/AppHost.cs') -Raw), 'AddDotnetProject\("([^"]+)", ProjectPath\("Norvia\.Integration\.Local\w+Worker"\)\)') | ForEach-Object { $_.Groups[1].Value }
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

    function script:Get-SecretData($Doc) {
        $data = [ordered]@{}
        foreach ($m in [regex]::Matches(($Doc.Text -split '(?m)^data:')[1], '(?m)^\s+(\S+): (\S+)\s*$')) {
            $data[$m.Groups[1].Value] = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($m.Groups[2].Value.Trim('"')))
        }
        $data
    }

    # The AppHost's statement that adds the worker (up to its semicolon).
    function script:Get-AppHostBlock([string] $Name) {
        # The statement ends at the first semicolon that ends a line and is not followed by a deeper-indented line: a lambda in it has semicolons of its own.
        [regex]::Match($script:appHost, "(?s)AddDotnetProject\(""$Name"", ProjectPath\(""Norvia\.Integration\.Local\w+Worker""\)\).*?;\r?\n(?! {8})").Value
    }

    $script:docs = Get-Rendered
    $script:appHost = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/AppHost.cs') -Raw
    $script:localTokensSource = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/LocalTokens.cs') -Raw
    $script:seed = Get-Content (Join-Path $script:root 'seed/norvia.seed.json') -Raw | ConvertFrom-Json
}

Describe 'the workers' -Skip:$script:helmMissing {
    It 'are the AppHost''s 14 Local*Worker hosts, one Deployment each' {
        $workerNames.Count | Should -Be 14
        $deployed = $docs | Where-Object { $_.Kind -eq 'Deployment' -and $_.Name -like '*-worker' } | ForEach-Object Name
        $deployed | Sort-Object | Should -Be ($workerNames | Sort-Object)
    }

    It '<_> is one replica that pulls its image from the configured registry' -ForEach $workerNames {
        $deployment = Get-Doc $docs 'Deployment' $_
        $deployment.Text | Should -Match '(?m)^\s+replicas: 1$'
        $deployment.Text | Should -Match "image: ${_}:latest"
        $rendered = Get-Rendered @('image.registry=registry.example.test/norvia', 'image.tag=1.2.3')
        (Get-Doc $rendered 'Deployment' $_).Text | Should -Match "image: registry\.example\.test/norvia/${_}:1\.2\.3"
    }

    It '<_> has the limits of the workers, and no port or probe unless it serves a trigger' -ForEach $workerNames {
        $deployment = Get-Doc $docs 'Deployment' $_
        $deployment.Text | Should -Match '(?s)limits:\s+memory: 256Mi'
        if ($_ -eq 'carrier-invoice-worker') {
            $deployment.Text | Should -Match '(?s)readinessProbe:\s+httpGet:\s+path: /health'
            $deployment.Text | Should -Match '(?s)livenessProbe:\s+httpGet:\s+path: /alive'
            (Get-Doc $docs 'Service' $_).Text | Should -Match 'port: 8080'
        } else {
            $deployment.Text | Should -Not -Match 'Probe:|containerPort|ASPNETCORE_HTTP_PORTS'
            Get-Doc $docs 'Service' $_ | Should -BeNullOrEmpty
        }
    }

    It '<_> has every setting the AppHost gives it, and each token constant as LocalTokens.cs has it' -ForEach $workerNames {
        $block = Get-AppHostBlock $_
        $block | Should -Not -BeNullOrEmpty
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $names = [regex]::Matches($block, 'WithEnvironment\("([A-Za-z_]+)"') | ForEach-Object { $_.Groups[1].Value } |
            Where-Object { $_ -notmatch '__DisabledFlagPath$' }
        $names.Count | Should -BeGreaterThan 2
        foreach ($name in $names) { $env.Contains($name) | Should -BeTrue -Because "the AppHost sets $name" }
        foreach ($m in [regex]::Matches($block, 'WithEnvironment\("([A-Za-z_]+)", LocalTokens\.(\w+)\)')) {
            $constant = [regex]::Match($localTokensSource, "public const string $($m.Groups[2].Value) = ""([^""]+)""").Groups[1].Value
            $constant | Should -Not -BeNullOrEmpty
            $env[$m.Groups[1].Value] | Should -Be $constant -Because $m.Groups[1].Value
        }
    }

    It '<_> reads the Service Bus emulator the AppHost gives it, as "messaging"' -ForEach $workerNames {
        $block = Get-AppHostBlock $_
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        if ($block -match 'WithReference\(messagingEdge, "messaging"\)') { $env['ConnectionStrings__messaging'] | Should -Match '^Endpoint=sb://messaging-2;' }
        elseif ($block -match 'WithReference\(messaging\)') { $env['ConnectionStrings__messaging'] | Should -Match '^Endpoint=sb://messaging;' }
        else { $env.Contains('ConnectionStrings__messaging') | Should -BeFalse }
    }

    It '<_> reads Azurite as the AppHost''s blob reference says' -ForEach $workerNames {
        $block = Get-AppHostBlock $_
        $env = Get-Env (Get-Doc $docs 'Deployment' $_)
        $name = if ($block -match 'WithReference\(blobs, "driver-app-blobs"\)') { 'ConnectionStrings__driver-app-blobs' } elseif ($block -match 'WithReference\(blobs\)') { 'ConnectionStrings__blobs' }
        if ($name) { $env[$name] | Should -Match 'BlobEndpoint=http://azurite:10000/devstoreaccount1;' }
        else { $env.Keys | Where-Object { $_ -like 'ConnectionStrings__*blobs' } | Should -BeNullOrEmpty }
    }

    It 'calls the TMS, ERP, Driver app and Partner portal by their Service names' {
        (Get-Env (Get-Doc $docs 'Deployment' 'booking-worker'))['Booking__TmsBaseAddress'] | Should -Be 'http://tms-api:8080'
        (Get-Env (Get-Doc $docs 'Deployment' 'billing-worker'))['Billing__ErpBaseAddress'] | Should -Be 'http://erp-api:8080'
        $dispatch = Get-Env (Get-Doc $docs 'Deployment' 'trip-dispatch-worker')
        $dispatch['TripDispatch__DriverApp__Address'] | Should -Be 'http://driver-app-api:8080/v1/trips'
        (Get-Env (Get-Doc $docs 'Deployment' 'driver-reports-worker'))['DriverReports__DriverApp__BaseAddress'] | Should -Be 'http://driver-app-api:8080'
        (Get-Env (Get-Doc $docs 'Deployment' 'booking-worker'))['Booking__Carriers__CAR-0001__Address'] | Should -Be 'http://partner-portal-web:8080/carrier/v1/CAR-0001/bookings'
    }

    It 'takes the key of the install from a Secret for every token it signs, as the AppHost gives it' {
        $signed = [regex]::Matches($appHost, '\((\w+Worker)\.Resource, "([A-Za-z_]+__SigningKey)"\)')
        $signed.Count | Should -Be 16
        foreach ($m in $signed) {
            $resource = [regex]::Match($appHost, "var $($m.Groups[1].Value) = builder\.AddDotnetProject\(""([^""]+)"", ProjectPath\([^)]*\)\)").Groups[1].Value
            (Get-Env (Get-Doc $docs 'Deployment' $resource))[$m.Groups[2].Value] | Should -Be 'secret:local-tokens/signing-key' -Because "$resource $($m.Groups[2].Value)"
        }
    }

    It 'calls each Persona''s endpoint with that Persona''s key, from the Secret, and holds no key itself' {
        foreach ($worker in 'booking-worker', 'carrier-cancellation-worker', 'carrier-invoice-outcome-worker', 'shipper-notification-worker', 'shipper-invoice-worker') {
            $deployment = Get-Doc $docs 'Deployment' $worker
            $deployment.Text | Should -Not -Match 'ApiKey\s+value:'
            $env = Get-Env $deployment
            $section = ($env.Keys | Where-Object { $_ -match '^(\w+)__(Carriers|Shippers)__' } | Select-Object -First 1) -replace '__(Carriers|Shippers)__.*', ''
            $type = if ($worker -like 'shipper-*') { 'Shipper' } else { 'Carrier' }
            $setting = "${type}s"
            foreach ($persona in $seed.personas | Where-Object { $_.party.type -eq $type }) {
                $env["${section}__${setting}__$($persona.party.id)__ApiKey"] | Should -Be "secret:endpoint-keys/${type}Endpoint__ApiKeys__$($persona.party.id)" -Because "$worker $($persona.party.id)"
            }
            ($env.Keys | Where-Object { $_ -like "*__${setting}__*__Address" }).Count | Should -Be @($seed.personas | Where-Object { $_.party.type -eq $type }).Count
        }
    }

    It 'sends each worker to the path the AppHost''s Persona gives its endpoint' {
        $paths = @{
            'booking-worker' = @('Carrier', 'bookings'); 'carrier-cancellation-worker' = @('Carrier', 'cancellations'); 'carrier-invoice-outcome-worker' = @('Carrier', 'invoice-outcomes')
            'shipper-notification-worker' = @('Shipper', 'notifications'); 'shipper-invoice-worker' = @('Shipper', 'invoices')
        }
        foreach ($worker in $paths.Keys) {
            $type, $path = $paths[$worker]
            $env = Get-Env (Get-Doc $docs 'Deployment' $worker)
            $addresses = $env.Keys | Where-Object { $_ -like '*__Address' -and $_ -match "__${type}s__" }
            foreach ($name in $addresses) { $env[$name] | Should -Match "^http://partner-portal-web:8080/$($type.ToLower())/v1/(CAR|SHP)-\d+/${path}$" -Because "$worker $name" }
        }
        $seedPersona = Get-Content (Join-Path $root 'src/Norvia.AppHost/SeedPersona.cs') -Raw
        foreach ($path in 'notifications', 'bookings', 'invoices', 'cancellations', 'invoice-outcomes') { $seedPersona | Should -Match "/v1/\{PartyId\}/$path" }
    }
}

Describe 'the endpoint keys' -Skip:$script:helmMissing {
    BeforeAll { $script:endpointKeys = Get-SecretData (Get-Doc $script:docs 'Secret' 'endpoint-keys') }

    It 'has a key for each Shipper and Carrier and one for the Driver app, all different, under the names of the settings that read them' {
        $names = @('DriverEndpoint__ApiKey') + @($seed.personas | ForEach-Object { "$($_.party.type)Endpoint__ApiKeys__$($_.party.id)" })
        $endpointKeys.Keys | Should -Be $names
        ($endpointKeys.Values | Select-Object -Unique).Count | Should -Be $names.Count
        $endpointKeys.Values | ForEach-Object { $_ | Should -Match '^[a-z0-9]{32}$' }
    }

    It 'is generated at each render, not written in the chart' {
        $again = Get-SecretData (Get-Doc (Get-Rendered) 'Secret' 'endpoint-keys')
        $again['DriverEndpoint__ApiKey'] | Should -Not -Be $endpointKeys['DriverEndpoint__ApiKey']
    }

    It 'gives the Trip dispatch worker the Driver app''s key' {
        (Get-Env (Get-Doc $docs 'Deployment' 'trip-dispatch-worker'))['TripDispatch__DriverApp__ApiKey'] | Should -Be 'secret:endpoint-keys/DriverEndpoint__ApiKey'
    }
}
