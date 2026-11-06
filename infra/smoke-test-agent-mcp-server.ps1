#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the example Agent MCP server (not in CI).
.DESCRIPTION
    Post-deploy smoke run of the example Agent MCP server (not in CI). Run it in a terminal, signed in to the Azure CLI as the
    presenter the estate was provisioned for (the presenter owns the example identities' app registrations, because the deployment
    runs as them), once `azd up` has deployed the estate:

      infra/smoke-test-agent-mcp-server.ps1

    It calls the example server (contracts/agent-mcp/example.tools.json) through API Management with the example identity's token and
    checks that:
      1. a call without a token, and a call with a token for another API, are refused with 401;
      2. an identity without the read role cannot even get a token for the agent view;
      3. tools/list with the example identity's token answers exactly the contract's tools;
      4. the one tool answers, with the correlation ID API Management created;
      5. a tool that is not on the list is refused with 403;
      6. the call is in Application Insights by that correlation ID, with the Agent's identity (azp) and the tool name.
    The example identity has no secret, so the script adds a one-hour secret to it, uses it for the tokens and removes it again. It
    prints names and counts only: never a token or a secret.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$contractPath = Join-Path $repo 'contracts/agent-mcp/example.tools.json'
$contract = Get-Content -Raw $contractPath | ConvertFrom-Json
$server = $contract.server
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
$gateway = $env:AZURE_ESTATE_PARTNER_API_URL -replace '/partner/v1$', ''
$mcp = "$gateway/agents/$server/mcp"

function Get-AppId([string] $Name) { (Invoke-Az ad app list --display-name "$Name ($($env:AZURE_ENV_NAME))" --query '[0].appId' --output tsv | Out-String).Trim() }
$audience = Get-AppId 'Norvia example agent view'
$caller = Get-AppId 'Norvia example agent identity'
$stranger = Get-AppId 'Norvia example identity without the role'
if (-not ($audience -and $caller -and $stranger)) { Write-Fail 'the example identities are not in Entra: has the estate been deployed (azd provision)?' }

$secretName = "smoke-agent-mcp-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"

function New-AppSecret([string] $App) {
    (Invoke-Az ad app credential reset --id $App --append --display-name $secretName --end-date ([DateTime]::UtcNow.AddHours(1).ToString('yyyy-MM-dd')) `
            --query password --output tsv | Out-String).Trim()
}
# A new secret takes a little while to be usable (AADSTS7000215 until then). The answer is the token, or the Entra error code.
function Get-AppToken([string] $App, [string] $Secret) {
    for ($attempt = 1; $attempt -le 12; $attempt++) {
        $answer = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token" -SkipHttpErrorCheck -Body @{
            client_id = $App; client_secret = $Secret; scope = "$audience/.default"; grant_type = 'client_credentials'
        }
        if ($answer.access_token) { return [pscustomobject]@{ Token = $answer.access_token; Error = $null } }
        $code = ([regex]::Match("$($answer.error_description)", 'AADSTS[0-9]+')).Value
        if ($code -ne 'AADSTS7000215') { return [pscustomobject]@{ Token = $null; Error = ($code ? $code : 'no-token') } }
        Start-Sleep -Seconds 10
    }
    [pscustomobject]@{ Token = $null; Error = 'AADSTS7000215' }
}

$jsonHeaders = @{ 'Accept' = 'application/json, text/event-stream' }
$toolsList = '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
function Get-McpStatus([hashtable] $Headers, [string] $Body = $toolsList) {
    (Invoke-WebRequest -Method Post -Uri $mcp -Headers ($jsonHeaders + $Headers) -ContentType 'application/json' -Body $Body -SkipHttpErrorCheck).StatusCode
}

try {
    Write-Host "Example MCP server: $mcp"

    # 1. No token, and a token for another API.
    $status = Get-McpStatus @{}
    if ($status -ne 401) { Write-Fail "a call without a token was answered $status, not 401" }
    $armToken = Get-AzAccessToken 'https://management.azure.com'
    $status = Get-McpStatus @{ Authorization = "Bearer $armToken" }
    if ($status -ne 401) { Write-Fail "a call with a token for another API was answered $status, not 401" }
    Write-Pass 'a call without a token and a call with a token for another API are refused (401)'

    # 2. An identity without the role gets no token for the view: Entra holds the view's service principal to assignment. (Were it to
    # get one, the server would still refuse it with 403.)
    $strangerToken = Get-AppToken $stranger (New-AppSecret $stranger)
    if ($strangerToken.Token) {
        $status = Get-McpStatus @{ Authorization = "Bearer $($strangerToken.Token)" }
        if ($status -ne 403) { Write-Fail "an identity without the role got a token, and the server answered $status, not 403" }
        Write-Pass 'an identity without the read role is refused by the server (403)'
    }
    else {
        if ($strangerToken.Error -ne 'AADSTS501051') { Write-Fail "the identity without the role was refused with $($strangerToken.Error), not AADSTS501051 (not assigned to a role)" }
        Write-Pass "an identity without the read role gets no token for the agent view ($($strangerToken.Error))"
    }

    # The example identity.
    $callerToken = Get-AppToken $caller (New-AppSecret $caller)
    if (-not $callerToken.Token) { Write-Fail "the example identity got no token for the agent view: $($callerToken.Error)" }
    $authorization = @{ Authorization = "Bearer $($callerToken.Token)" }

    # An MCP call: JSON-RPC over streamable HTTP. The answer is JSON or one server-sent event.
    $script:session = $null
    function Invoke-McpCall([string] $Body) {
        $headers = $jsonHeaders + $authorization
        if ($script:session) { $headers['Mcp-Session-Id'] = $script:session }
        $response = Invoke-WebRequest -Method Post -Uri $mcp -Headers $headers -ContentType 'application/json' -Body $Body -SkipHttpErrorCheck
        $text = $response.Content -is [byte[]] ? [Text.Encoding]::UTF8.GetString($response.Content) : "$($response.Content)"
        $data = @($text -split "`n" | ForEach-Object { $_.TrimEnd("`r") } | Where-Object { $_ -match '^data: ' -or $_ -match '^[{\[]' } | ForEach-Object { $_ -replace '^data: ', '' })
        [pscustomobject]@{ Response = $response; Body = ($data.Count -gt 0 ? ($data[0] | ConvertFrom-Json) : $null) }
    }
    $initialize = Invoke-McpCall '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}'
    $script:session = @($initialize.Response.Headers['Mcp-Session-Id'])[0]
    Invoke-McpCall '{"jsonrpc":"2.0","method":"notifications/initialized"}' | Out-Null

    # 3. The tool list is the contract's.
    $listed = (@((Invoke-McpCall '{"jsonrpc":"2.0","id":2,"method":"tools/list"}').Body.result.tools.name) | Sort-Object) -join ','
    $expected = (@($contract.tools.name) | Sort-Object) -join ','
    if ($listed -ne $expected) { Write-Fail "tools/list answered '$listed', the contract says '$expected'" }
    Write-Pass "tools/list answers the contract's tools: $listed"

    # 4. The tool answers, and the answer carries the correlation ID.
    $tool = $contract.tools[0].name
    $call = Invoke-McpCall (@{ jsonrpc = '2.0'; id = 3; method = 'tools/call'; params = @{ name = $tool; arguments = @{} } } | ConvertTo-Json -Compress -Depth 5)
    if (-not ($call.Body.result -and -not $call.Body.result.isError)) {
        Write-Fail "the tool $tool did not answer: $(($call.Body.error ?? $call.Body.result) | ConvertTo-Json -Compress -Depth 5)"
    }
    $correlationId = @($call.Response.Headers['X-Correlation-ID'])[0]
    if (-not $correlationId) { Write-Fail 'the answer carries no correlation ID' }
    Write-Pass "$tool answered (correlation ID $correlationId)"

    # 5. A tool that is not on the list.
    $status = Get-McpStatus ($authorization + ($script:session ? @{ 'Mcp-Session-Id' = $script:session } : @{})) `
        '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"delete_everything","arguments":{}}}'
    if ($status -ne 403) { Write-Fail "a tool that is not on the list was answered $status, not 403" }
    Write-Pass 'a tool that is not on the list is refused (403)'

    # 6. The hop is in Application Insights with the Agent's identity and the tool name. Ingestion takes a few minutes: ask again for up to 10.
    $workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup
    $hops = Wait-Until {
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace -Timespan PT1H "AppTraces | where TimeGenerated > ago(30m) | where tostring(Properties.correlationId) == '$correlationId' | where tostring(Properties.agentId) == '$caller' and tostring(Properties.toolName) == '$tool' | count"
        ($rows.Count -gt 0 -and [int] $rows[0][0] -gt 0) ? $rows[0][0] : $null
    }
    if (-not $hops) { Write-Fail 'the call is not in Application Insights with agentId and toolName within 10 minutes' }
    Write-Pass "the call is on its Message trail with the Agent's identity and the tool name"
}
finally {
    foreach ($app in $caller, $stranger) {
        $key = (Invoke-Az ad app credential list --id $app --query "[?displayName=='$secretName'].keyId | [0]" --output tsv -AllowFailure | Out-String).Trim()
        if ($key) { Invoke-Az ad app credential delete --id $app --key-id $key --output none -AllowFailure | Out-Null }
    }
}
