#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook, after every publish (infra/deploy-agents.ps1): gives each published Agent's identity the one read
    role of its API, an app role on the API's app registration, and checks that the project's shared identity holds none
    (ADR 0040).
.DESCRIPTION
    Foundry makes a new identity on every publish, so the role is assigned again every time. The superseded identity
    keeps its role until Foundry deletes it with the version (docs/operations/foundry.md, "When a publish gives a new
    identity").

    The input is what deploy-agents.ps1 writes after publishing, one entry per Agent:
        [{ "agent": "integration-operations", "server": "example", "agentIdentityId": "<object ID of the agent identity>" }]
    The server names a contract (contracts/agent-mcp/<server>.tools.json), which holds the role. The audience, the API's
    app registration, is "Norvia <server> agent view (<env>)"; the example's is "Norvia example agent view (<env>)".

    Which credential: the Graph call needs AppRoleAssignment.ReadWrite.All and Application.Read.All. The signed-in `az`
    token is used unless AGENT_ROLES_CLIENT_ID is set, in which case the deployment app registration signs in with client
    credentials (a secret in AGENT_ROLES_CLIENT_SECRET, or a federated credential's assertion in
    AGENT_ROLES_CLIENT_ASSERTION in CI). Why, and what is known and not: docs/operations/foundry.md, "Agent roles: which
    credential". It prints names and counts only, never a token.
.PARAMETER Published
    The published-agents.json to read. Defaults to AGENT_PUBLISH_RESULT, then .azure/<env>/published-agents.json.
#>
[CmdletBinding()]
param([Parameter(Position = 0)][string] $Published)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Published) {
    $Published = $env:AGENT_PUBLISH_RESULT ? $env:AGENT_PUBLISH_RESULT : (Join-Path $repo '.azure' $env:AZURE_ENV_NAME 'published-agents.json')
}
if (-not (Test-Path -Path $Published -PathType Leaf)) { Write-Fail "No published agents at ${Published}: run infra/deploy-agents.ps1 first." }
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()
$graph = 'https://graph.microsoft.com/v1.0'

function Get-GraphToken {
    if ($env:AGENT_ROLES_CLIENT_ID) {
        $body = @{
            client_id  = $env:AGENT_ROLES_CLIENT_ID
            scope      = 'https://graph.microsoft.com/.default'
            grant_type = 'client_credentials'
        }
        if ($env:AGENT_ROLES_CLIENT_ASSERTION) {
            $body.client_assertion = $env:AGENT_ROLES_CLIENT_ASSERTION
            $body.client_assertion_type = 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer'
        }
        elseif ($env:AGENT_ROLES_CLIENT_SECRET) {
            $body.client_secret = $env:AGENT_ROLES_CLIENT_SECRET
        }
        else {
            Write-Fail 'AGENT_ROLES_CLIENT_ID is set without a client secret or a federated assertion to sign in with'
        }
        try {
            (Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token" -Body $body).access_token
        }
        catch {
            # The error may echo the request: say only that the sign-in failed.
            Write-Fail "the client credentials of $($env:AGENT_ROLES_CLIENT_ID) were refused by Entra"
        }
    }
    else {
        (Invoke-Az account get-access-token --resource-type ms-graph --query accessToken --output tsv | Out-String).Trim()
    }
}
$token = Get-GraphToken

function Invoke-Graph {
    # Returns the response body; on an error stops with Graph's error code. Never prints the token.
    param([Parameter(Mandatory)][string] $Method, [Parameter(Mandatory)][string] $Path, $Body)
    $request = @{
        Method                  = $Method
        Uri                     = "$graph$Path"
        Headers                 = @{ Authorization = "Bearer $token" }
        SkipHttpErrorCheck      = $true
        StatusCodeVariable      = 'status'
    }
    if ($null -ne $Body) {
        $request.Body = $Body | ConvertTo-Json -Depth 10 -Compress
        $request.ContentType = 'application/json'
    }
    $response = Invoke-RestMethod @request
    if ($status -lt 200 -or $status -ge 300) {
        $detail = $response.error ? "$($response.error.code): $($response.error.message)" : 'no-detail'
        throw "Graph $Method $Path answered ${status}: $detail"
    }
    $response
}

$assigned = 0
foreach ($entry in @(Get-Content -Path $Published -Raw | ConvertFrom-Json)) {
    $agent = $entry.agent
    $identity = $entry.agentIdentityId
    $role = (Get-Content -Path (Join-Path $repo 'contracts' 'agent-mcp' "$($entry.server).tools.json") -Raw | ConvertFrom-Json).role

    $viewName = "Norvia $($entry.server) agent view ($($env:AZURE_ENV_NAME))"
    $filter = [uri]::EscapeDataString("displayName eq '$viewName'")
    $view = Invoke-Graph -Method GET -Path ('/servicePrincipals?$filter=' + $filter + '&$select=id,appRoles')
    $viewId = @($view.value)[0].id
    if (-not $viewId) { Write-Fail "${agent}: the service principal '$viewName' is not in Entra: has the estate been deployed?" }
    $roleId = @(@($view.value)[0].appRoles | Where-Object { $_.value -eq $role })[0].id
    if (-not $roleId) { Write-Fail "${agent}: '$viewName' has no app role $role" }

    # The identity's assignments on this view. Another identity of the same Agent (an earlier publish) is removed below.
    $current = Invoke-Graph -Method GET -Path ('/servicePrincipals/' + $viewId + '/appRoleAssignedTo?$top=999')
    if (@($current.value | Where-Object { $_.principalId -eq $identity -and $_.appRoleId -eq $roleId }).Count -gt 0) {
        Write-Pass "$agent ($identity) already holds $role"
    }
    else {
        try {
            Invoke-Graph -Method POST -Path "/servicePrincipals/$viewId/appRoleAssignedTo" `
                -Body @{ principalId = $identity; resourceId = $viewId; appRoleId = $roleId } | Out-Null
        }
        catch {
            Write-Fail "${agent}: the role assignment was refused ($($_.Exception.Message)). See docs/operations/foundry.md, 'Agent roles: which credential'"
        }
        Write-Pass "$agent ($identity) now holds $role"
    }
    $assigned++
}

# The project's shared identity holds no role on any agent view: an unpublished Agent can call nothing.
$resourceGroup = Get-ResourceGroupName
$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$projectIdentity = (Invoke-Az resource show --resource-group $resourceGroup --namespace Microsoft.CognitiveServices --resource-type projects `
        --parent "accounts/$account" --name norvia --query identity.principalId --output tsv | Out-String).Trim()
if ($projectIdentity) {
    $held = @((Invoke-Graph -Method GET -Path "/servicePrincipals/$projectIdentity/appRoleAssignments").value).Count
    if ($held -ne 0) { Write-Fail "the project's shared identity $projectIdentity holds $held app role(s): it must hold none" }
    Write-Pass "the project's shared identity holds no app role"
}

Write-Host "$assigned Agent(s) hold their read role."
