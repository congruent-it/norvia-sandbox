#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the namespace's protection (#938): the network policies, the internal-only ingress, the Secrets' RBAC, the
# requests and limits and the quota. The seam is the output of `helm template`, so nothing here needs a cluster; that a CNI enforces
# the policies is what the smoke script's isolation check settles.
BeforeDiscovery {
    $script:helmMissing = -not (Get-Command helm -ErrorAction SilentlyContinue)
}

BeforeAll {
    $script:root = Split-Path $PSScriptRoot -Parent
    $script:chart = Join-Path $script:root 'deploy/helm/norvia'

    function script:Get-Rendered {
        param([string[]] $Set = @(), [switch] $AllowFailure)
        $arguments = @('template', 'norvia', $script:chart, '--namespace', 'norvia', '--set', 'sql.saPassword=Test-Passw0rd', '--set', 'edge.baseDomain=norvia.test')
        foreach ($s in $Set) { $arguments += '--set', $s }
        $output = & helm @arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            if ($AllowFailure) { return ($output -join "`n") }
            throw "helm template failed: $output"
        }
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

    # A quantity in the unit of its kind: cores for cpu, bytes for memory and storage.
    function script:ConvertTo-Quantity([string] $Text) {
        if ($Text -notmatch '^(\d+(?:\.\d+)?)(m|Ki|Mi|Gi|Ti)?$') { throw "cannot read the quantity '$Text'" }
        $factor = switch ($Matches[2]) { 'm' { 0.001 } 'Ki' { 1KB } 'Mi' { 1MB } 'Gi' { 1GB } 'Ti' { 1TB } default { 1 } }
        [double]$Matches[1] * $factor
    }

    # The containers of a pod template, each as { Requests, Limits } of name -> quantity, in the order they are written.
    function script:Get-ContainerResources($Doc) {
        $containers = ($Doc.Text -split '(?m)^\s+containers:\s*$', 2)[1]
        if (-not $containers) { return @() }
        # What follows the containers (volumes, a StatefulSet's claim templates, whose requests are storage) is not theirs.
        $containers = ($containers -split '(?m)^\s+(?:volumeClaimTemplates|volumes):\s*$', 2)[0]
        $images = [regex]::Matches($containers, '(?m)^\s+image: ').Count
        $blocks = [regex]::Matches($containers, '(?ms)^( +)resources:\n((?:\1 +\S[^\n]*\n?)+)')
        if ($blocks.Count -ne $images) { return $null }
        foreach ($block in $blocks) {
            $result = [pscustomobject]@{ Requests = @{}; Limits = @{} }
            $section = $null
            foreach ($line in $block.Groups[2].Value -split '\n' | Where-Object { $_.Trim() }) {
                if ($line -match '^\s+(limits|requests):\s*$') { $section = $Matches[1]; continue }
                if ($line -match '^\s+(\S+): "?([^"\s]+)"?\s*$') { $result.($section.Substring(0, 1).ToUpper() + $section.Substring(1))[$Matches[1]] = ConvertTo-Quantity $Matches[2] }
            }
            $result
        }
    }

    $script:docs = Get-Rendered
}

Describe 'the default-deny network policy' -Skip:$script:helmMissing {
    BeforeAll {
        $script:deny = Get-Doc $script:docs 'NetworkPolicy' 'default-deny'
    }

    It 'selects every pod and denies ingress and egress' {
        $deny | Should -Not -BeNullOrEmpty
        $deny.Text | Should -Match '(?m)^\s+podSelector: \{\}'
        $deny.Text | Should -Match '(?m)^\s+- Ingress$'
        $deny.Text | Should -Match '(?m)^\s+- Egress$'
        $deny.Text | Should -Not -Match '(?m)^\s+(ingress|egress):'
    }
}

Describe 'what the default-deny policy lets through' -Skip:$script:helmMissing {
    It 'lets pods of the namespace talk to each other, and nothing else in' {
        $policy = Get-Doc $docs 'NetworkPolicy' 'allow-same-namespace'
        $policy.Text | Should -Match '(?s)ingress:\s+- from:\s+- podSelector: \{\}'
        $policy.Text | Should -Match '(?s)egress:\s+- to:\s+- podSelector: \{\}'
    }

    It 'lets pods resolve names through the cluster DNS, on 53 over UDP and TCP' {
        $policy = Get-Doc $docs 'NetworkPolicy' 'allow-dns'
        $policy.Text | Should -Match '(?s)egress:.*kubernetes\.io/metadata\.name: kube-system.*k8s-app: kube-dns'
        $policy.Text | Should -Match '(?s)protocol: UDP\s+port: 53'
        $policy.Text | Should -Match '(?s)protocol: TCP\s+port: 53'
    }

    It 'lets the ingress controller reach the exposed applications on the app port, and only them' {
        $policy = Get-Doc $docs 'NetworkPolicy' 'allow-ingress-controller'
        $policy.Text | Should -Match '(?s)kubernetes\.io/metadata\.name: kube-system'
        $policy.Text | Should -Match '(?s)app\.kubernetes\.io/name\s+operator: In\s+values:\s+- partner-api\s+- tms-api\s+- tms-web\s+- erp-api\s+- erp-web\s+- driver-app-api\s+- driver-app-web\s+- integration-console-web\s+- health-check-web'
        $policy.Text | Should -Not -Match 'agent-mcp'
        $policy.Text | Should -Match '(?s)port: 8080'
        (Get-Doc (Get-Rendered @('agents.enabled=true')) 'NetworkPolicy' 'allow-ingress-controller').Text | Should -Match '- agent-mcp'
    }

    It 'takes the ingress controller''s namespace from a value' {
        $rendered = Get-Rendered @('networkPolicy.ingressController.namespace=ingress-nginx')
        (Get-Doc $rendered 'NetworkPolicy' 'allow-ingress-controller').Text | Should -Match 'kubernetes\.io/metadata\.name: ingress-nginx'
    }

    It 'lets only the pods that wait for the migrations reach the API server' {
        $policy = Get-Doc $docs 'NetworkPolicy' 'allow-api-server'
        $policy.Text | Should -Match 'norvia\.io/waits-for-migrations: "true"'
        $policy.Text | Should -Match 'cidr: 0\.0\.0\.0/0'
        $policy.Text | Should -Match '(?s)port: 6443'
        $narrow = Get-Rendered @('networkPolicy.apiServer.cidrs={10.0.0.5/32}')
        (Get-Doc $narrow 'NetworkPolicy' 'allow-api-server').Text | Should -Match 'cidr: 10\.0\.0\.5/32'
    }

    It 'admits no other namespace, and no address range, anywhere else' {
        $policies = $docs | Where-Object Kind -eq 'NetworkPolicy'
        $allowed = 'default-deny', 'allow-same-namespace', 'allow-dns', 'allow-ingress-controller', 'allow-api-server'
        $policies.Name | Sort-Object | Should -Be ($allowed | Sort-Object)
        foreach ($policy in $policies | Where-Object { $_.Name -notin 'allow-dns', 'allow-ingress-controller' }) {
            $policy.Text | Should -Not -Match 'namespaceSelector'
        }
        ($policies | Where-Object { $_.Name -ne 'allow-api-server' }).Text -join "`n" | Should -Not -Match 'ipBlock'
    }
}

Describe 'the internal-only ingress' -Skip:$script:helmMissing {
    It 'admits only private address ranges through a Traefik Middleware by default, on every Ingress' {
        $middleware = Get-Doc $docs 'Middleware' 'internal-only'
        $middleware.Text | Should -Match 'apiVersion: traefik\.io/v1alpha1'
        $middleware.Text | Should -Match '(?s)ipAllowList:\s+sourceRange:\s+- 10\.0\.0\.0/8\s+- 172\.16\.0\.0/12\s+- 192\.168\.0\.0/16'
        foreach ($ingress in $docs | Where-Object Kind -eq 'Ingress') {
            $ingress.Text | Should -Match 'traefik\.ingress\.kubernetes\.io/router\.middlewares: norvia-internal-only@kubernetescrd'
        }
    }

    It 'takes the address ranges from a value' {
        $rendered = Get-Rendered @('edge.internal.allowedCidrs={100.64.0.0/10}')
        (Get-Doc $rendered 'Middleware' 'internal-only').Text | Should -Match '- 100\.64\.0\.0/10'
    }

    It 'sets the allow list as an annotation for ingress-nginx, with no Middleware' {
        $rendered = Get-Rendered @('edge.internal.controller=nginx', 'edge.ingress.className=nginx')
        $rendered | Where-Object Kind -eq 'Middleware' | Should -BeNullOrEmpty
        (Get-Doc $rendered 'Ingress' 'partner-api').Text | Should -Match 'nginx\.ingress\.kubernetes\.io/whitelist-source-range: "?10\.0\.0\.0/8,172\.16\.0\.0/12,192\.168\.0\.0/16"?'
    }

    It 'uses the ingress class of the internal controller when there is one' {
        $rendered = Get-Rendered @('edge.internal.controller=none', 'edge.internal.className=traefik-lan')
        $rendered | Where-Object Kind -eq 'Middleware' | Should -BeNullOrEmpty
        (Get-Doc $rendered 'Ingress' 'partner-api').Text | Should -Match 'ingressClassName: traefik-lan'
    }

    It 'refuses to render when nothing would keep the Ingress internal' {
        (Get-Rendered @('edge.internal.controller=none') -AllowFailure) | Should -Match 'edge\.internal'
    }

    It 'can be turned off, which leaves the Ingress as the cluster''s ingress controller serves it' {
        $rendered = Get-Rendered @('edge.internal.enabled=false')
        $rendered | Where-Object Kind -eq 'Middleware' | Should -BeNullOrEmpty
        (Get-Doc $rendered 'Ingress' 'partner-api').Text | Should -Not -Match 'internal-only|whitelist-source-range'
    }
}

Describe 'who can read the Secrets' -Skip:$script:helmMissing {
    BeforeAll {
        $script:role = Get-Doc $script:docs 'Role' 'secret-reader'
        $script:binding = Get-Doc $script:docs 'RoleBinding' 'secret-reader'
    }

    It 'lets a Role get each Secret the chart makes by name, and nothing else of Secrets' {
        $role | Should -Not -BeNullOrEmpty
        $names = [regex]::Matches(($role.Text -split 'resourceNames:')[1], '(?m)^\s+- ([\w-]+)\s*$') | ForEach-Object { $_.Groups[1].Value }
        $names | Sort-Object | Should -Be (($docs | Where-Object Kind -eq 'Secret').Name | Sort-Object)
        $role.Text | Should -Match '(?s)verbs:\s+- get\s*$|verbs: \["get"\]'
        $role.Text | Should -Not -Match 'list|watch|create|update|patch|delete|\*'
    }

    It 'binds the Role to the norvia service account and to the subjects the operator names' {
        $binding.Text | Should -Match '(?s)kind: ServiceAccount\s+name: norvia'
        $rendered = Get-Rendered @('rbac.secretReaders[0].kind=Group', 'rbac.secretReaders[0].name=norvia-operators', 'rbac.secretReaders[0].apiGroup=rbac.authorization.k8s.io')
        (Get-Doc $rendered 'RoleBinding' 'secret-reader').Text | Should -Match '(?s)kind: Group\s+name: norvia-operators'
    }

    It 'runs every pod as a service account without a token, but the one that waits for the migrations' {
        $sa = Get-Doc $docs 'ServiceAccount' 'norvia'
        $sa.Text | Should -Match 'automountServiceAccountToken: false'
        $pods = $docs | Where-Object { $_.Kind -in 'Deployment', 'StatefulSet', 'Job' }
        $pods | Should -Not -BeNullOrEmpty
        # Only the applications that wait for a migrations Job keep their token; a pod that gains the label by mistake fails here.
        ($pods | Where-Object { $_.Text -match 'norvia\.io/waits-for-migrations: "true"' } | ForEach-Object Name | Sort-Object) | Should -Be @('driver-app-api', 'driver-app-web', 'erp-api', 'erp-web', 'tms-api', 'tms-web')
        foreach ($pod in $pods) {
            if ($pod.Text -match 'norvia\.io/waits-for-migrations: "true"') {
                $pod.Text | Should -Match '(?m)^\s+serviceAccountName: migrations-waiter$' -Because "$($pod.Kind) $($pod.Name) waits for the migrations, so its init container needs the API"
                $pod.Text | Should -Not -Match 'automountServiceAccountToken: false' -Because "$($pod.Kind) $($pod.Name) keeps its token for the waiter"
                continue
            }
            $pod.Text | Should -Match '(?m)^\s+serviceAccountName: norvia$' -Because "$($pod.Kind) $($pod.Name) must not run as the namespace's default"
            $pod.Text | Should -Match '(?m)^\s+automountServiceAccountToken: false$' -Because "$($pod.Kind) $($pod.Name) has no use for an API token"
        }
    }
}

Describe 'requests and limits' -Skip:$script:helmMissing {
    BeforeAll {
        $script:workloads = $script:docs | Where-Object { $_.Kind -in 'Deployment', 'StatefulSet', 'Job' }
    }

    It 'gives every container of every Deployment, StatefulSet and migrations Job a CPU and memory request and a memory limit' {
        $workloads | Should -Not -BeNullOrEmpty
        foreach ($workload in $workloads) {
            $containers = @(Get-ContainerResources $workload)
            $containers | Should -Not -BeNullOrEmpty -Because "$($workload.Kind) $($workload.Name) must have containers with resources"
            foreach ($container in $containers) {
                $container.Requests['cpu'] | Should -BeGreaterThan 0 -Because "$($workload.Name) needs a CPU request"
                $container.Requests['memory'] | Should -BeGreaterThan 0 -Because "$($workload.Name) needs a memory request"
                $container.Limits['memory'] | Should -BeGreaterOrEqual $container.Requests['memory'] -Because "$($workload.Name) needs a memory limit of at least its request"
            }
        }
    }

    It 'holds SQL Server''s own memory cap, MSSQL_MEMORY_LIMIT_MB, below the container''s limit' {
        $sql = Get-Doc $docs 'StatefulSet' 'sql'
        $cap = [double][regex]::Match($sql.Text, '(?s)MSSQL_MEMORY_LIMIT_MB\s+value: "?(\d+)').Groups[1].Value * 1MB
        $cap | Should -BeGreaterThan 0
        (Get-ContainerResources $sql)[0].Limits['memory'] | Should -BeGreaterThan $cap
    }
}

Describe 'the LimitRange and the ResourceQuota' -Skip:$script:helmMissing {
    BeforeAll {
        $script:limitRange = Get-Doc $script:docs 'LimitRange' 'defaults'
        $script:quota = Get-Doc $script:docs 'ResourceQuota' 'norvia'
        $script:hard = @{}
        foreach ($m in [regex]::Matches(($script:quota.Text -split '(?m)^\s+hard:')[1], '(?m)^\s+(\S+): "?([^"\s]+)"?\s*$')) { $script:hard[$m.Groups[1].Value] = $m.Groups[2].Value }
    }

    It 'gives a container that names no requests or limit (an init container, a pod added later) the defaults the quota needs' {
        $limitRange.Text | Should -Match '(?s)type: Container\s+.*'
        $limitRange.Text | Should -Match '(?s)default:\s+memory: \S+'
        $limitRange.Text | Should -Match '(?s)defaultRequest:\s+cpu: \S+\s+memory: \S+'
        $limitRange.Text | Should -Not -Match '(?s)default:\s+cpu'
    }

    It 'caps the pods, the CPU and memory requested, the memory limited and the volumes of the namespace' {
        foreach ($key in 'pods', 'requests.cpu', 'requests.memory', 'limits.memory', 'persistentvolumeclaims', 'requests.storage') {
            $hard.Keys | Should -Contain $key
        }
        $hard.Keys | Should -Not -Contain 'limits.cpu' -Because 'the chart sets no CPU limits'
    }

    It 'is large enough for everything the chart deploys, the migrations Jobs running at once included' {
        foreach ($with in @(@(), @('agents.enabled=true'))) {
            $rendered = Get-Rendered $with
            $pods = $rendered | Where-Object { $_.Kind -in 'Deployment', 'StatefulSet', 'Job' }
            $requestsCpu = 0; $requestsMemory = 0; $limitsMemory = 0
            foreach ($pod in $pods) {
                foreach ($container in Get-ContainerResources $pod) {
                    $requestsCpu += $container.Requests['cpu']; $requestsMemory += $container.Requests['memory']; $limitsMemory += $container.Limits['memory']
                }
            }
            $storage = 0
            foreach ($m in [regex]::Matches((($rendered | Where-Object Kind -eq 'StatefulSet').Text -join "`n"), '(?m)^\s+storage: (\S+)\s*$')) { $storage += ConvertTo-Quantity $m.Groups[1].Value }
            $requestsCpu | Should -BeLessThan (ConvertTo-Quantity $hard['requests.cpu'])
            $requestsMemory | Should -BeLessThan (ConvertTo-Quantity $hard['requests.memory'])
            $limitsMemory | Should -BeLessThan (ConvertTo-Quantity $hard['limits.memory'])
            @($pods).Count | Should -BeLessThan ([int]$hard['pods'])
            $storage | Should -BeLessThan (ConvertTo-Quantity $hard['requests.storage'])
            ($rendered | Where-Object Kind -eq 'StatefulSet').Count | Should -BeLessThan ([int]$hard['persistentvolumeclaims'])
        }
    }

    It 'takes its sizes from values, and is left out when the cluster sets its own' {
        $rendered = Get-Rendered @('quota.hard.pods=7', 'quota.hard.requests\.cpu=9')
        (Get-Doc $rendered 'ResourceQuota' 'norvia').Text | Should -Match 'pods: "?7"?'
        (Get-Rendered @('quota.enabled=false')) | Where-Object { $_.Kind -in 'ResourceQuota', 'LimitRange' } | Should -BeNullOrEmpty
    }
}

Describe 'the namespace and what is cluster-scoped' -Skip:$script:helmMissing {
    BeforeAll {
        $script:clusterScoped = 'ClusterRole', 'ClusterRoleBinding', 'CustomResourceDefinition', 'IngressClass', 'StorageClass', 'ClusterIssuer', 'PersistentVolume',
            'ValidatingWebhookConfiguration', 'MutatingWebhookConfiguration', 'PriorityClass'
    }

    It 'installs nothing cluster-scoped: no controller, issuer, storage class, CRD or cluster role' {
        foreach ($set in @(@(), @('agents.enabled=true'), @('edge.internal.controller=nginx'), @('namespace.create=true'))) {
            $kinds = (Get-Rendered $set).Kind | Sort-Object -Unique
            $kinds | Where-Object { $_ -in $clusterScoped } | Should -BeNullOrEmpty
        }
    }

    It 'puts nothing in another namespace' {
        foreach ($doc in $docs) { $doc.Text | Should -Not -Match '(?m)^  namespace: (?!norvia$)' -Because "$($doc.Kind) $($doc.Name)" }
    }

    It 'leaves the namespace to Helm by default, which cannot store a release in a namespace it has not made' {
        $docs | Where-Object Kind -eq 'Namespace' | Should -BeNullOrEmpty
    }

    It 'makes and labels the namespace on request, for a GitOps tool that applies what the chart renders' {
        $namespace = Get-Doc (Get-Rendered @('namespace.create=true')) 'Namespace' 'norvia'
        $namespace.Text | Should -Match 'app\.kubernetes\.io/part-of: norvia'
        $namespace.Text | Should -Match 'pod-security\.kubernetes\.io/enforce: baseline'
        $custom = Get-Rendered @('namespace.create=true', 'namespace.labels.team=platform')
        (Get-Doc $custom 'Namespace' 'norvia').Text | Should -Match 'team: platform'
    }
}

Describe 'init containers and the pods that wait for the migrations' -Skip:$script:helmMissing {
    It 'gives every init container requests and a memory limit of its own, so none runs unbounded when the LimitRange is off' {
        $withInit = $script:docs | Where-Object { $_.Text -match '(?m)^\s+initContainers:' }
        $withInit | Should -Not -BeNullOrEmpty
        foreach ($doc in $withInit) {
            $section = ($doc.Text -split '(?m)^\s+initContainers:\s*$', 2)[1]
            $section = ($section -split '(?m)^\s+containers:\s*$', 2)[0]
            $images = [regex]::Matches($section, '(?m)^\s+image: ').Count
            [regex]::Matches($section, '(?m)^\s+resources:\s*$').Count | Should -Be $images -Because "$($doc.Kind) $($doc.Name) has $images init containers"
            $section | Should -Match '(?s)limits:\s+memory: \S+\s+requests:\s+cpu: \S+\s+memory: \S+'
        }
    }

    It 'labels the pod of an application that waits for the migrations, which allow-api-server selects, and keeps its token' {
        # No application of the chart waits yet: render one through a copy of the chart with a template that includes the helper as one will.
        $copy = Join-Path $TestDrive 'norvia'
        Copy-Item $script:chart $copy -Recurse
        Set-Content (Join-Path $copy 'templates/zz-waiter.yaml') '{{ include "norvia.deployment" (dict "root" . "name" "partner-api" "env" "" "waitsForMigrations" true) }}'
        $output = & helm template norvia $copy --namespace norvia --set sql.saPassword=x --set edge.baseDomain=norvia.test --show-only templates/zz-waiter.yaml 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output -join "`
")
        $text = $output -join "`
"
        $text | Should -Match '(?m)^\s+serviceAccountName: migrations-waiter$'
        $text | Should -Match 'norvia\.io/waits-for-migrations: "true"'
        $text | Should -Not -Match 'automountServiceAccountToken'
        (Get-Doc $docs 'Deployment' 'partner-api').Text | Should -Not -Match 'waits-for-migrations'
    }
}
