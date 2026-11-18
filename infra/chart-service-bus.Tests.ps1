#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's Service Bus emulators (#927). The seam is the output of `helm template`, so nothing here needs a
# cluster. The topology is checked against infra/estate/messaging.bicep and the AppHost's ResubmitAwareFilters, the two places it is
# written already, so a subscription changed there and not here fails here.
BeforeDiscovery {
    $script:helmMissing = -not (Get-Command helm -ErrorAction SilentlyContinue)
}

BeforeAll {
    $script:root = Split-Path $PSScriptRoot -Parent
    $script:chart = Join-Path $script:root 'deploy/helm/norvia'

    function script:Get-Rendered {
        param([string[]] $Set = @(), [string] $Path = $script:chart)
        $arguments = @('template', 'norvia', $Path, '--namespace', 'norvia', '--set', 'sql.saPassword=Test-Passw0rd', '--set', 'edge.baseDomain=norvia.test')
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

    # The Config.json an emulator reads, parsed from its ConfigMap.
    function script:Get-EmulatorConfig {
        param($Docs, [string] $Name)
        $text = (Get-Doc $Docs 'ConfigMap' "$Name-config").Text
        $json = [regex]::Match($text, '(?s)Config\.json: \|\r?\n(.*)').Groups[1].Value
        ($json -replace '(?m)^ {4}', '') | ConvertFrom-Json -Depth 20
    }

    # Every subscription of the namespace, with the name of its topic added.
    function script:Get-Subscriptions($Config) {
        foreach ($topic in $Config.UserConfig.Namespaces[0].Topics) {
            foreach ($subscription in $topic.Subscriptions) { $subscription | Add-Member -NotePropertyName Topic -NotePropertyValue $topic.Name -PassThru -Force }
        }
    }

    $script:docs = Get-Rendered
    $script:messaging = Get-EmulatorConfig $script:docs 'messaging'
    $script:messaging2 = Get-EmulatorConfig $script:docs 'messaging-2'
    $script:bicep = Get-Content (Join-Path $script:root 'infra/estate/messaging.bicep') -Raw
    $script:filters = Get-Content (Join-Path $script:root 'src/Norvia.AppHost/ResubmitAwareFilters.cs') -Raw
}

Describe 'the Service Bus emulators' -Skip:$script:helmMissing {
    It '<_> is one Deployment reached as the Service <_>, on the AMQP port and the management port' -ForEach 'messaging', 'messaging-2' {
        $deployment = Get-Doc $docs 'Deployment' $_
        $deployment | Should -Not -BeNullOrEmpty
        $deployment.Text | Should -Match '(?m)^\s+replicas: 1$'
        $service = (Get-Doc $docs 'Service' $_).Text
        $service | Should -Match 'port: 5672'
        $service | Should -Match 'port: 5300'
    }

    It '<_> keeps its state in the data tier SQL Server, with the password from the Secret' -ForEach 'messaging', 'messaging-2' {
        $text = (Get-Doc $docs 'Deployment' $_).Text
        $text | Should -Match '(?s)name: SQL_SERVER\s+value: "?sql"?'
        $text | Should -Match '(?s)name: ACCEPT_EULA\s+value: "Y"'
        $text | Should -Match '(?s)MSSQL_SA_PASSWORD\s+valueFrom:\s+secretKeyRef:\s+name: sql\s+key: sa-password'
        $text | Should -Match 'image: mcr\.microsoft\.com/azure-messaging/servicebus-emulator:'
    }

    It '<_> waits for SQL Server before it starts' -ForEach 'messaging', 'messaging-2' {
        $text = (Get-Doc $docs 'Deployment' $_).Text
        $text | Should -Match 'initContainers:'
        $text | Should -Match 'sqlcmd.*-S sql,1433'
    }

    It '<_> reads its Config.json from its ConfigMap, and rolls when that changes' -ForEach 'messaging', 'messaging-2' {
        $text = (Get-Doc $docs 'Deployment' $_).Text
        $text | Should -Match 'mountPath: /ServiceBus_Emulator/ConfigFiles/Config\.json'
        $text | Should -Match 'subPath: Config\.json'
        $text | Should -Match "name: $_-config"
        $text | Should -Match 'checksum/config: [0-9a-f]{64}'
    }

    It 'is ready when the management port says so' {
        (Get-Doc $docs 'Deployment' 'messaging').Text | Should -Match '(?s)readinessProbe:\s+httpGet:\s+path: /health\s+port: 5300'
    }

    It 'pulls the emulator image from the value' {
        $custom = Get-Rendered -Set 'serviceBus.image=registry.example.com/sb:1'
        (Get-Doc $custom 'Deployment' 'messaging').Text | Should -Match 'image: registry\.example\.com/sb:1'
    }
}

Describe 'the topology of messaging' -Skip:$script:helmMissing {
    It 'has the topics the AppHost starts messaging with' {
        ($messaging.UserConfig.Namespaces[0].Topics.Name | Sort-Object) | Should -Be @('shipment-events', 'transport-order-events', 'trip-events')
        $messaging.UserConfig.Namespaces[0].Queues | Should -BeNullOrEmpty
    }

    It 'gives the TMS topics the subscriptions of the Bicep' {
        $subs = Get-Subscriptions $messaging | ForEach-Object { "$($_.Topic)/$($_.Name)" } | Sort-Object
        $subs | Should -Be @(
            'shipment-events/carrier-booking', 'shipment-events/carrier-cancellation', 'shipment-events/carrier-cost',
            'shipment-events/monitoring', 'shipment-events/shipper-notification',
            'transport-order-events/billing', 'transport-order-events/monitoring',
            'trip-events/monitoring', 'trip-events/trip-dispatch')
    }
}

Describe 'the topology of messaging-2' -Skip:$script:helmMissing {
    It 'has the topics and queues of the Driver app, the ERP and the Carrier tracking path' {
        ($messaging2.UserConfig.Namespaces[0].Topics.Name | Sort-Object) | Should -Be @('driver-app-events', 'invoicing-events', 'master-data-events')
        ($messaging2.UserConfig.Namespaces[0].Queues.Name | Sort-Object) | Should -Be @('carrier-proof-of-delivery', 'carrier-tracking')
    }

    It 'gives its topics the subscriptions of the Bicep' {
        $subs = Get-Subscriptions $messaging2 | ForEach-Object { "$($_.Topic)/$($_.Name)" } | Sort-Object
        $subs | Should -Be @(
            'driver-app-events/driver-reports',
            'invoicing-events/carrier-invoice-outcome', 'invoicing-events/monitoring', 'invoicing-events/shipper-invoice',
            'master-data-events/master-data-sync', 'master-data-events/monitoring')
    }

    It 'makes driver-reports a session subscription, and nothing else' {
        $subs = Get-Subscriptions $messaging2
        ($subs | Where-Object { $_.Properties.RequiresSession }).Name | Should -Be 'driver-reports'
    }

    It 'redelivers a queue message ten times before dead-lettering it' {
        foreach ($queue in $messaging2.UserConfig.Namespaces[0].Queues) { $queue.Properties.MaxDeliveryCount | Should -Be 10 }
    }
}

Describe 'the subscription filters' -Skip:$script:helmMissing {
    BeforeAll {
        $script:all = @(Get-Subscriptions $messaging) + @(Get-Subscriptions $messaging2)
    }

    It 'gives <Name> its SQL rule, named resubmit-aware' -ForEach @(
        @{ Name = 'monitoring' }, @{ Name = 'billing' }, @{ Name = 'carrier-booking' }, @{ Name = 'shipper-notification' },
        @{ Name = 'carrier-cancellation' }, @{ Name = 'carrier-cost' }, @{ Name = 'trip-dispatch' }, @{ Name = 'driver-reports' },
        @{ Name = 'shipper-invoice' }, @{ Name = 'carrier-invoice-outcome' }, @{ Name = 'master-data-sync' }
    ) {
        $rules = ($all | Where-Object Name -eq $Name).Rules
        @($rules).Count | Should -BeGreaterOrEqual 1
        foreach ($rule in @($rules)) {
            $rule.Name | Should -Be 'resubmit-aware'
            $rule.Properties.FilterType | Should -Be 'Sql'
        }
    }

    It 'matches the Bicep expression of every subscription, character for character' {
        # Every rule in the Bicep, in the form name -> expression, per subscription resource.
        $expected = [regex]::Matches($script:bicep, "(?s)resource \w+Filter '[^']+' = \{\s+parent: (\w+)\s+name: 'resubmit-aware'.*?sqlExpression: '((?:[^'\\]|\\.)*)'")
        $expected.Count | Should -Be 15
        $bicepExpressions = $expected | ForEach-Object { ($_.Groups[2].Value -replace "\\'", "'") } | Sort-Object
        $chartExpressions = $all | ForEach-Object { $_.Rules[0].Properties.SqlFilter.SqlExpression } | Sort-Object
        $chartExpressions | Should -Be $bicepExpressions
    }

    It 'matches the conditions the AppHost gives the local emulator' {
        $expected = [regex]::Matches($script:filters, '\["([a-z-]+)"\] = "([^"]+)"')
        $expected.Count | Should -Be 10
        foreach ($m in $expected) {
            $name = $m.Groups[1].Value
            $condition = $m.Groups[2].Value
            $sub = $all | Where-Object Name -eq $name
            # AND binds tighter than OR, so the chart (like the Bicep) brackets a condition that has an OR in it. The AppHost's class
            # does not, which lets a resubmit aimed at another subscription through; that is its bug, not a difference to copy.
            $bracketed = if ($condition -match ' OR ') { "($condition)" } else { $condition }
            $sub.Rules[0].Properties.SqlFilter.SqlExpression | Should -Be "($bracketed AND resubmitTo IS NULL) OR resubmitTo = '$name'" -Because $name
        }
    }
}

Describe 'the connection strings' -Skip:$script:helmMissing {
    BeforeAll {
        $script:copy = Join-Path $TestDrive 'norvia'
        Copy-Item $script:chart $script:copy -Recurse
        Set-Content (Join-Path $script:copy 'templates/fixture.yaml') @'
apiVersion: v1
kind: ConfigMap
metadata:
  name: bus-fixture
data:
  messaging: {{ include "norvia.serviceBusConnectionString" (dict "root" . "name" "messaging") | quote }}
  messaging-2: {{ include "norvia.serviceBusConnectionString" (dict "root" . "name" "messaging-2") | quote }}
  management: {{ include "norvia.serviceBusManagementConnectionString" (dict "root" . "name" "messaging-2") | quote }}
'@
        $script:fixture = (Get-Doc (Get-Rendered -Path $script:copy) 'ConfigMap' 'bus-fixture').Text
    }

    It 'points an application at the emulator Service, with the emulator flag' {
        $fixture | Should -Match 'messaging: "Endpoint=sb://messaging;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"'
        $fixture | Should -Match 'messaging-2: "Endpoint=sb://messaging-2;'
    }

    It 'gives the Integration console the management port, apart from the one the apps use' {
        $fixture | Should -Match 'management: "Endpoint=sb://messaging-2:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"'
    }
}

Describe 'the rendered chart with the emulators' -Skip:$script:helmMissing {
    It 'passes helm lint' {
        $output = & helm lint $script:chart --set sql.saPassword=Test-Passw0rd --set edge.baseDomain=norvia.test 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output -join "`n")
    }

    It 'is valid Kubernetes YAML (kubeconform)' -Skip:(-not (Get-Command kubeconform -ErrorAction SilentlyContinue)) {
        $manifests = & helm template norvia $script:chart --namespace norvia --set sql.saPassword=Test-Passw0rd --set edge.baseDomain=norvia.test
        $output = $manifests | & kubeconform -strict -summary 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output -join "`n")
    }
}

Describe 'the lock durations' -Skip:$script:helmMissing {
    It 'gives every subscription and queue the lock duration the Bicep gives it, and no other one' {
        $entities = @(Get-Subscriptions $messaging) + @(Get-Subscriptions $messaging2) + @($messaging2.UserConfig.Namespaces[0].Queues)
        $actual = $entities | Where-Object { $_.Properties.LockDuration } | ForEach-Object { "$($_.Name)=$($_.Properties.LockDuration)" } | Sort-Object
        # Every lockDuration in the Bicep, in the file's order: three subscriptions, then the two queues.
        $bicepDurations = [regex]::Matches($script:bicep, "lockDuration: '(PT\w+)'") | ForEach-Object { $_.Groups[1].Value }
        $bicepDurations.Count | Should -Be 5
        $actual | Should -Be @('billing=PT5M', 'carrier-cost=PT5M', 'carrier-proof-of-delivery=PT5M', 'carrier-tracking=PT1M', 'driver-reports=PT5M')
        (($bicepDurations | Sort-Object) -join ',') | Should -Be ((($actual | ForEach-Object { $_ -replace '.*=' }) | Sort-Object) -join ',')
    }
}
