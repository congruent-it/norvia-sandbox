#Requires -Version 7.4
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

# Template tests for the chart's data tier (#926): SQL Server, Azurite and the migrations Jobs. The seam is the output of
# `helm template`, so nothing here needs a cluster. Needs helm (the dev container has it, ADR 0053).
BeforeDiscovery {
    $script:helmMissing = -not (Get-Command helm -ErrorAction SilentlyContinue)
}

BeforeAll {
    $script:chart = Join-Path (Split-Path $PSScriptRoot -Parent) 'deploy/helm/norvia'

    # Renders the chart and returns its documents as { Kind; Name; Text }.
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
}

Describe 'the data tier' -Skip:$script:helmMissing {
    Context 'SQL Server' {
        BeforeAll { $script:docs = Get-Rendered }

        It 'is one StatefulSet named sql, with its data on a PVC' {
            $sql = Get-Doc $docs 'StatefulSet' 'sql'
            $sql | Should -Not -BeNullOrEmpty
            $sql.Text | Should -Match '(?m)^\s+replicas: 1$'
            $sql.Text | Should -Match 'volumeClaimTemplates:'
            $sql.Text | Should -Match 'mountPath: /var/opt/mssql'
        }

        It 'uses the cluster default storage class unless a value names one' {
            (Get-Doc $docs 'StatefulSet' 'sql').Text | Should -Not -Match 'storageClassName'
            $named = Get-Rendered -Set 'persistence.storageClass=local-path'
            (Get-Doc $named 'StatefulSet' 'sql').Text | Should -Match 'storageClassName: "?local-path"?'
        }

        It 'is held to the memory the AppHost gives it' {
            (Get-Doc $docs 'StatefulSet' 'sql').Text | Should -Match '(?s)MSSQL_MEMORY_LIMIT_MB\s+value: "1024"'
        }

        It 'is reachable as the service sql on 1433, and reads its password from a Secret' {
            (Get-Doc $docs 'Service' 'sql').Text | Should -Match 'port: 1433'
            (Get-Doc $docs 'Secret' 'sql').Text | Should -Match 'VGVzdC1QYXNzdzByZA=='
            (Get-Doc $docs 'StatefulSet' 'sql').Text | Should -Match '(?s)MSSQL_SA_PASSWORD\s+valueFrom:\s+secretKeyRef:\s+name: sql'
        }
    }
}

Describe 'Azurite' -Skip:$script:helmMissing {
    BeforeAll { $script:docs = Get-Rendered }

    It 'is a StatefulSet with the blobs on a PVC of its own' {
        $azurite = Get-Doc $docs 'StatefulSet' 'azurite'
        $azurite | Should -Not -BeNullOrEmpty
        $azurite.Text | Should -Match 'volumeClaimTemplates:'
        $azurite.Text | Should -Match 'mountPath: /data'
        $azurite.Text | Should -Match '--location'
    }

    It 'serves only the blob service, on every interface' {
        $text = (Get-Doc $docs 'StatefulSet' 'azurite').Text
        $text | Should -Match 'azurite-blob'
        $text | Should -Match '(?s)--blobHost\s+- "?0\.0\.0\.0"?'
        $text | Should -Not -Match 'azurite-queue|azurite-table'
    }

    It 'is reachable as the service azurite on 10000' {
        (Get-Doc $docs 'Service' 'azurite').Text | Should -Match 'port: 10000'
    }

    It 'uses the storage class value too' {
        $named = Get-Rendered -Set 'persistence.storageClass=local-path'
        (Get-Doc $named 'StatefulSet' 'azurite').Text | Should -Match 'storageClassName: "?local-path"?'
    }
}

Describe 'the migrations' -Skip:$script:helmMissing {
    BeforeAll {
        $script:docs = Get-Rendered
    }

    It 'runs the migrations of <_> as a Job, not a Deployment' -ForEach 'tms', 'erp', 'driver-app', 'partner-portal', 'simulator' {
        $project = $_
        $job = Get-Doc $docs 'Job' "$project-migrations-1"
        $job | Should -Not -BeNullOrEmpty
        $job.Text | Should -Match 'restartPolicy: OnFailure'
        $docs | Where-Object { $_.Kind -eq 'Deployment' -and $_.Name -like "$project-migrations*" } | Should -BeNullOrEmpty
    }

    It 'is not a Helm hook, so helm --wait does not wait on the applications first' {
        ($docs | Where-Object Kind -eq 'Job').Text | Should -Not -Match 'helm\.sh/hook'
    }

    It 'gives the <_> migrations the connection string of their own database, with the password from the Secret' -ForEach 'tms', 'driver-app' {
        $text = (Get-Doc $docs 'Job' "$_-migrations-1").Text
        $text | Should -Match "ConnectionStrings__$_"
        $text | Should -Match "Server=sql,1433;Database=$_;User ID=sa;Password=\`$\(MSSQL_SA_PASSWORD\);TrustServerCertificate=true"
        $text | Should -Match '(?s)MSSQL_SA_PASSWORD\s+valueFrom:\s+secretKeyRef:\s+name: sql'
    }

    It 'creates the database first, once, even when the name has a hyphen' {
        $text = (Get-Doc $docs 'Job' 'driver-app-migrations-1').Text
        $text | Should -Match 'initContainers:'
        $text | Should -Match 'DB_ID\(N''driver-app''\) IS NULL CREATE DATABASE \[driver-app\]'
    }

    It 'pulls the image from the registry value, with the tag value' {
        $custom = Get-Rendered -Set 'image.registry=registry.example.com/norvia/', 'image.tag=1.2.3'
        (Get-Doc $custom 'Job' 'tms-migrations-1').Text | Should -Match 'image: registry\.example\.com/norvia/tms-migrations:1\.2\.3'
        (Get-Doc $docs 'Job' 'tms-migrations-1').Text | Should -Match 'image: tms-migrations:latest'
    }

    It 'lets a pod read Jobs, and nothing else, to wait for them' {
        $role = Get-Doc $docs 'Role' 'migrations-waiter'
        $role.Text | Should -Match 'jobs'
        $role.Text | Should -Match '(?s)verbs:.*get.*watch'
        $role.Text | Should -Not -Match 'secrets|create|delete'
        (Get-Doc $docs 'ServiceAccount' 'migrations-waiter') | Should -Not -BeNullOrEmpty
        (Get-Doc $docs 'RoleBinding' 'migrations-waiter') | Should -Not -BeNullOrEmpty
    }

    Context 'an application that includes norvia.waitForMigrations' {
        BeforeAll {
            # A copy of the chart with one extra template standing in for an application slice.
            $script:copy = Join-Path $TestDrive 'norvia'
            Copy-Item $script:chart $script:copy -Recurse
            Set-Content (Join-Path $script:copy 'templates/fixture.yaml') @'
apiVersion: v1
kind: Pod
metadata:
  name: fixture
spec:
  serviceAccountName: migrations-waiter
  initContainers:
    {{- include "norvia.waitForMigrations" (dict "root" . "names" (list "tms" "erp")) | nindent 4 }}
  containers:
    - name: app
      image: app
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: azurite-fixture
data:
  blobs: {{ include "norvia.azuriteConnectionString" . | quote }}
'@
            $rendered = Get-Rendered -Path $script:copy
            $script:fixture = (Get-Doc $rendered 'Pod' 'fixture').Text
            $script:azurite = (Get-Doc $rendered 'ConfigMap' 'azurite-fixture').Text
        }

        It 'waits for each named migration Job to complete' {
            $script:fixture | Should -Match 'job/tms-migrations-1'
            $script:fixture | Should -Match 'job/erp-migrations-1'
            $script:fixture | Should -Match '--for=condition=complete'
        }

        It 'runs kubectl directly, since the kubectl image has no shell, and lets the kubelet retry until the Job exists' {
            $script:fixture | Should -Not -Match '/bin/sh'
            $script:fixture | Should -Match '(?s)command:\s+- kubectl\s+- wait'
        }

        It 'hands the application the Azurite connection string the apps read' {
            $script:azurite | Should -Match 'DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;'
        }
    }
}

Describe 'the rendered chart' -Skip:$script:helmMissing {
    It 'passes helm lint' {
        $output = & helm lint $script:chart --set sql.saPassword=Test-Passw0rd --set edge.baseDomain=norvia.test 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output -join "`n")
    }

    It 'is valid Kubernetes YAML (kubeconform)' -Skip:(-not (Get-Command kubeconform -ErrorAction SilentlyContinue)) {
        $manifests = & helm template norvia $script:chart --namespace norvia --set sql.saPassword=Test-Passw0rd --set edge.baseDomain=norvia.test
        $output = $manifests | & kubeconform -strict -ignore-missing-schemas -summary 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output -join "`n")
    }
}
