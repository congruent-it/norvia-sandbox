#Requires -Version 7.4
<#
.SYNOPSIS
    The helpers of the Kubernetes smoke script (ADR 0048, ADR 0053): local tokens, ingress URLs, waiting and named steps.
.DESCRIPTION
    Import it beside Norvia.Infra.psm1, whose Write-Pass and Write-Fail it uses:
        Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force
        Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Kubernetes.psm1') -Force
    Nothing here needs a cluster; the script reads the cluster's Secrets itself.
#>

Set-StrictMode -Version Latest

function ConvertTo-Base64Url {
    <# .SYNOPSIS Base64url, as a JWT writes its parts. #>
    param([Parameter(Mandatory)][byte[]] $Bytes)
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-LocalToken {
    <#
    .SYNOPSIS A token signed as the local stand-ins sign theirs (src/Norvia.AppHost/LocalTokens.cs): HS256, valid for an hour.
    .DESCRIPTION
        Locally there is no Entra, so the apps trust tokens signed with one key. The chart keeps it in the Secret local-tokens, key
        signing-key, as a base64 string that the apps decode into the key's bytes. -SigningKey is that string.
    .PARAMETER ClientId The client in azp: a Persona's (norvia-local-persona-<id>) or an identity's (norvia-local-simulator).
    .PARAMETER Roles The app roles. With none, the claim is left out, as Entra leaves it out.
    #>
    param(
        [Parameter(Mandatory)][string] $SigningKey,
        [Parameter(Mandatory)][string] $Issuer,
        [Parameter(Mandatory)][string] $Audience,
        [Parameter(Mandatory)][string] $ClientId,
        [string[]] $Roles = @()
    )
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $payload = [ordered]@{ iss = $Issuer; aud = $Audience; azp = $ClientId; iat = $now; nbf = $now; exp = $now + 3600 }
    if ($Roles.Count -gt 0) { $payload.roles = $Roles }
    $encode = { param($Object) ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(($Object | ConvertTo-Json -Compress))) }
    $signed = "$(& $encode ([ordered]@{ alg = 'HS256'; typ = 'JWT' })).$(& $encode $payload)"
    $hmac = [Security.Cryptography.HMACSHA256]::new([Convert]::FromBase64String($SigningKey))
    try { "$signed.$(ConvertTo-Base64Url $hmac.ComputeHash([Text.Encoding]::ASCII.GetBytes($signed)))" }
    finally { $hmac.Dispose() }
}

function Get-IngressUrl {
    <# .SYNOPSIS The URL of an application's Ingress: <application>.<base domain>, as templates/ingress.yaml names it. #>
    param(
        [Parameter(Mandatory)][string] $Application,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string] $BaseDomain,
        [switch] $Insecure
    )
    "$($Insecure ? 'http' : 'https')://$Application.$BaseDomain"
}

function Test-SameStatus {
    <# .SYNOPSIS Whether a status is the expected one, ignoring case, spaces and hyphens ("In transit" is what the TMS may spell InTransit). #>
    param($Actual, [Parameter(Mandatory)][string] $Expected)
    if ($null -eq $Actual) { return $false }
    $normalise = { param($Text) ([string] $Text) -replace '[\s\-_]', '' }
    (& $normalise $Actual) -ieq (& $normalise $Expected)
}

function Wait-ForValue {
    <#
    .SYNOPSIS Runs a condition until it returns something, and returns that. Running out fails, naming what was waited for.
    .PARAMETER Describe Runs once when the attempts are used up and says what was last seen, so the failure holds it.
    #>
    param(
        [Parameter(Mandatory)][string] $Description,
        [Parameter(Mandatory)][scriptblock] $Condition,
        [scriptblock] $Describe,
        [ValidateRange(1, 10000)][int] $Attempts = 36,
        [ValidateRange(0, 3600)][int] $IntervalSeconds = 5
    )
    $value = Wait-Until -Condition $Condition -Attempts $Attempts -IntervalSeconds $IntervalSeconds
    if ($null -ne $value) { return $value }
    $seen = $Describe ? " ($(& $Describe))" : ''
    throw "timed out after $($Attempts * $IntervalSeconds) seconds waiting for $Description$seen"
}

function Invoke-SmokeStep {
    <#
    .SYNOPSIS Runs one step of the smoke run. It prints PASS and returns what the step returned, or fails naming the step.
    #>
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][scriptblock] $Action
    )
    try { $result = & $Action }
    catch { Write-Fail "${Name}: $($_.Exception.Message)" }
    Write-Pass $Name
    $result
}


function Invoke-Kubectl {
    <# .SYNOPSIS Runs kubectl and returns its output; a non-zero exit stops with kubectl's own message (as Invoke-Az does for az). #>
    param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]] $Arguments)
    $output = & kubectl @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "kubectl $($Arguments -join ' ') failed: $($output -join ' ')" }
    ($output -join "`n")
}

function Get-SecretValue {
    <# .SYNOPSIS One key of a Secret, decoded. #>
    param([Parameter(Mandatory)][string] $Namespace, [Parameter(Mandatory)][string] $Secret, [Parameter(Mandatory)][string] $Key)
    $encoded = Invoke-Kubectl get secret $Secret --namespace $Namespace --output "jsonpath={.data['$Key']}"
    if (-not $encoded) { throw "the Secret $Secret has no key $Key" }
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded))
}

function Invoke-Call {
    <# .SYNOPSIS One HTTP call. Never throws for a status; returns { Status, Body (parsed, or $null), CorrelationId }. #>
    param([Parameter(Mandatory)][string] $Method, [Parameter(Mandatory)][string] $Uri, [hashtable] $Headers = @{}, $Body = $null, [switch] $SkipCertificateCheck)
    $arguments = @{ Method = $Method; Uri = $Uri; Headers = $Headers; SkipHttpErrorCheck = $true; TimeoutSec = 30 }
    if ($SkipCertificateCheck) { $arguments.SkipCertificateCheck = $true }
    if ($null -ne $Body) { $arguments.Body = ($Body | ConvertTo-Json -Depth 20 -Compress); $arguments.ContentType = 'application/json' }
    $response = Invoke-WebRequest @arguments
    $parsed = try { $response.Content | ConvertFrom-Json -Depth 20 -AsHashtable } catch { $null }
    [pscustomobject]@{ Status = [int] $response.StatusCode; Body = $parsed; CorrelationId = @($response.Headers['X-Correlation-ID'])[0] }
}

function Format-Answer {
    <# .SYNOPSIS What a failing call says, without its body: the status, and the problem's title, code, text or message. #>
    param([Parameter(Mandatory)] $Answer)
    $detail = if ($Answer.Body -is [hashtable]) { ($Answer.Body['title'], $Answer.Body['code'], $Answer.Body['text'], $Answer.Body['message'] | Where-Object { $_ }) -join ', ' }
    "answered $($Answer.Status)$($detail ? " ($detail)" : '')"
}

function Assert-Accepted {
    <#
    .SYNOPSIS Throws, naming who answered what, unless the answer is a 2xx (or exactly -Status), and with a correlation ID when asked.
    #>
    param([Parameter(Mandatory)] $Answer, [Parameter(Mandatory)][string] $What, [int] $Status, [switch] $RequireCorrelationId)
    $accepted = $Status ? $Answer.Status -eq $Status : ($Answer.Status -ge 200 -and $Answer.Status -lt 300)
    if (-not $accepted) { throw "$What $(Format-Answer $Answer)$($Status ? ", not $Status Accepted" : '')" }
    if ($RequireCorrelationId -and -not $Answer.CorrelationId) { throw "$What accepted the call but it carries no X-Correlation-ID" }
}

function Get-IsolationTargets {
    <# .SYNOPSIS What no other namespace may reach (#938): the Partner API, SQL Server and the two Service Bus emulators, as host:port from another namespace. #>
    param([Parameter(Mandatory)][string] $Namespace)
    foreach ($target in @(@('partner-api', 8080), @('sql', 1433), @('messaging', 5672), @('messaging-2', 5672))) { "$($target[0]).$Namespace.svc.cluster.local:$($target[1])" }
}

function Test-CanConnect {
    <#
    .SYNOPSIS Whether a throwaway pod in a namespace can open a TCP connection to host:port. A pod that cannot start (no image, no rights) is also false.
    .PARAMETER Image An image with /agnhost, whose connect command exits non-zero when it cannot connect (registry.k8s.io/e2e-test-images/agnhost).
    #>
    param(
        [Parameter(Mandatory)][string] $Namespace,
        [Parameter(Mandatory)][string] $Target,
        [Parameter(Mandatory)][string] $Image,
        [ValidateRange(1, 300)][int] $TimeoutSeconds = 5
    )
    $name = "connect-probe-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    # kubectl directly, not Invoke-Kubectl: a non-zero exit is the answer here (cannot connect), not an error.
    $null = & kubectl run $name --namespace $Namespace --image $Image --restart=Never --rm -i --quiet --command -- /agnhost connect $Target "--timeout=${TimeoutSeconds}s" 2>&1
    $LASTEXITCODE -eq 0
}

function Assert-Isolated {
    <#
    .SYNOPSIS Throws unless a pod of the namespace reaches the targets and a pod of another namespace reaches none of them.
    .DESCRIPTION
        The default-deny NetworkPolicy of the chart (#938) is only as good as the cluster's CNI, so the smoke script asks the cluster. The pod
        inside the namespace comes first: if it cannot connect either, the probe (its image, the cluster's pull rights) is what failed, and a
        refusal from the other namespace would prove nothing.
    #>
    param(
        [Parameter(Mandatory)][string] $Namespace,
        [Parameter(Mandatory)][string] $ProbeNamespace,
        [Parameter(Mandatory)][string[]] $Target,
        [Parameter(Mandatory)][string] $Image,
        # Reachable from every namespace (the cluster DNS, over TCP): a probe that connects here has run, so its refusals are the policy's.
        [string] $ControlTarget = 'kube-dns.kube-system.svc.cluster.local:53'
    )
    if (-not (Test-CanConnect -Namespace $Namespace -Target $Target[0] -Image $Image)) {
        throw "the probe pod could not connect from inside the namespace $Namespace to $($Target[0]), so a refusal from another namespace would prove nothing (is the image ${Image} pullable, and the application running?)"
    }
    if (-not (Test-CanConnect -Namespace $ProbeNamespace -Target $ControlTarget -Image $Image)) {
        throw "the probe pod could not connect from the namespace $ProbeNamespace to $ControlTarget, which every namespace may reach, so its refusals prove nothing (can that namespace pull ${Image}?)"
    }
    $reached = @($Target | Where-Object { Test-CanConnect -Namespace $ProbeNamespace -Target $_ -Image $Image })
    if ($reached.Count -gt 0) { throw "a pod in the namespace $ProbeNamespace reached $($reached -join ', '): the namespace is not isolated (does the cluster's CNI enforce NetworkPolicy?)" }
}

Export-ModuleMember -Function ConvertTo-Base64Url, Get-LocalToken, Get-IngressUrl, Test-SameStatus, Wait-ForValue, Invoke-SmokeStep, Invoke-Kubectl, Get-SecretValue, Invoke-Call, Format-Answer, Assert-Accepted, Get-IsolationTargets, Test-CanConnect, Assert-Isolated
