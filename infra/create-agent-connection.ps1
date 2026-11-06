#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook: the Foundry project's connection to an API Management MCP server, with the agent-identity
    authentication type and the server's audience. Foundry then calls the server with the token of the Agent that makes
    the call (its own identity, made when it is published), never the project's shared identity.
.DESCRIPTION
    A server is the name in its contract (contracts/agent-mcp/<server>.tools.json). The connection is named
    "<server>-mcp"; the target is the server's address on the gateway, and the audience is the agent view's app
    registration ("Norvia <server> agent view (<env>)"). Running it again updates the connection.

    Why a script: the Bicep connection resource (Microsoft.CognitiveServices/accounts/projects/connections, API versions
    up to 2026-07-01) lists no agent-identity authType (AAD, AccessKey, AccountKey, ApiKey, CustomKeys, ManagedIdentity,
    None, OAuth2, PAT, SAS, ServicePrincipal, UsernamePassword only), so Bicep cannot make this connection
    (docs/operations/foundry.md). When it can, delete this script and use Bicep.
.PARAMETER Server
    The servers to make a connection to.
.EXAMPLE
    ./create-agent-connection.ps1 integration-operations health-check
#>
[CmdletBinding()]
param([Parameter(Mandatory, Position = 0, ValueFromRemainingArguments)][string[]] $Server)
$ErrorActionPreference = 'Stop'
$PSNativeCommandArgumentPassing = 'Standard'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT
if (-not $endpoint) { Write-Fail 'No Foundry project endpoint: has the estate been deployed (azd provision)?' }
$gateway = $env:AZURE_ESTATE_PARTNER_API_URL -replace '/partner/v1$', ''

& azd ai project set $endpoint | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Fail "azd ai project set failed with exit code $LASTEXITCODE" }

foreach ($name in $Server) {
    if (-not (Test-Path -Path (Join-Path $repo 'contracts' 'agent-mcp' "$name.tools.json") -PathType Leaf)) {
        Write-Fail "No contract for the server $name."
    }
    $audience = (Invoke-Az ad app list --display-name "Norvia $name agent view ($($env:AZURE_ENV_NAME))" `
            --query '[0].appId' --output tsv | Out-String).Trim()
    if (-not $audience) { Write-Fail "The agent view of $name is not in Entra: has the estate been deployed?" }
    $target = "$gateway/agents/$name/mcp"
    & azd ai connection create "$name-mcp" --kind remote-tool --target $target --auth-type agentic-identity --audience $audience --force
    if ($LASTEXITCODE -ne 0) { Write-Fail "azd ai connection create $name-mcp failed with exit code $LASTEXITCODE" }
    Write-Host "Connection $name-mcp`: $target (audience $audience)"
}
