#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy hook, last: publishes the Agents to Foundry from their definition files (ADR 0040), after the apps are
    deployed.
.DESCRIPTION
    No names publishes every agents/*.agent.yaml. For each Agent it
      1. points the Agent's MCP server at the application's address in Azure (the Integration operations agent's is the
         Integration console's, the Estate health-check agent's is the Estate health check's, the Customer service agent's is
         the TMS API's, the Carrier invoice matching agent's is the ERP API's; the estate could not know any of them when it was deployed): the API Management backend, and
         ESTATE_INTEGRATION_CONSOLE_BASE_URL, ESTATE_HEALTH_CHECK_BASE_URL, ESTATE_TMS_BASE_URL or ESTATE_ERP_BASE_URL so deploying the estate again
         keeps it;
      2. makes the project's connection to the MCP server, with the agent-identity authentication type
         (infra/create-agent-connection.ps1);
      3. creates the Agent's next version as a prompt agent in the Foundry project, from the file: the model, the
         instructions, the temperature, the MCP server as its one tool (read-only, no approval to ask for) and the
         correlation ID as a structured input that the tool's X-Correlation-ID header carries; the MCP server makes its
         own correlation ID for each call and records this one as the runId, so the Agent's tool calls are found from the
         run it was asked about;
      4. reads the Agent's identity (Foundry gives each Agent its own) and writes .azure/<env>/published-agents.json;
      5. gives each identity its one read role (infra/assign-agent-roles.ps1), which a new identity needs after every
         publish.
    The Intake agent (booking-intake) has no tool, so it has no server, connection or role: it is published into its own
    Foundry project, norvia-intake (FOUNDRY_INTAKE_PROJECT_ENDPOINT), which has no Application Insights connection, and the
    API Management backend of the Booking draft host (booking-draft-host) is pointed at the host's address
    (ESTATE_BOOKING_DRAFT_BASE_URL) so the Partner API's booking-drafts operation reaches it.
    It prints names, versions and counts only: never a token. Publishing the same file again is a new version of the same
    Agent. The definition files are YAML, which PowerShell does not read: `yq` turns each into JSON.
.PARAMETER Name
    The Agents to publish. None publishes every agents/*.agent.yaml.
.EXAMPLE
    ./deploy-agents.ps1 integration-operations
#>
[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments)][string[]] $Name = @())
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$endpoint = $env:FOUNDRY_PROJECT_ENDPOINT ? $env:FOUNDRY_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT
$published = Join-Path $repo '.azure' $env:AZURE_ENV_NAME 'published-agents.json'
$gateway = $env:AZURE_ESTATE_PARTNER_API_URL -replace '/partner/v1$', ''
$apiVersion = 'v1'
# The Agents that read a Shipper's text (the Intake agent's booking email, the tracking assistant's question) are published into the Intake project, which has no Application Insights connection
# (docs/operations/foundry.md). Every other Agent is published into the norvia project.
$intakeAgents = @('booking-intake', 'shipper-tracking-assistant')
$intakeEndpoint = $env:FOUNDRY_INTAKE_PROJECT_ENDPOINT ? $env:FOUNDRY_INTAKE_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_INTAKE_PROJECT_ENDPOINT

if (-not (Get-Command yq -ErrorAction SilentlyContinue)) { Write-Fail 'yq is not installed' }
if (-not $endpoint) { Write-Fail 'no Foundry project endpoint: has the estate been deployed (azd provision)?' }
if (-not $gateway) { Write-Fail 'no API Management address: has the estate been deployed (azd provision)?' }

$files = $Name.Count -gt 0 ? @($Name | ForEach-Object { Join-Path $repo 'agents' "$_.agent.yaml" }) :
@(Get-ChildItem -Path (Join-Path $repo 'agents') -Filter '*.agent.yaml' | Sort-Object Name | ForEach-Object FullName)

function Invoke-Foundry {
    # Returns the response body; on an error stops with the status and Foundry's error code.
    param([Parameter(Mandatory)][string] $Method, [Parameter(Mandatory)][string] $Path, $Body, [string] $ProjectEndpoint = $endpoint)
    # A new project's Agent service can answer 404 ResourceNotFound ("Subdomain does not map to a resource") for a while
    # after the project is made, and then work. Nothing here asks for something that may be absent, so a 404 is tried again.
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        $token = (Invoke-Az account get-access-token --resource https://ai.azure.com --query accessToken --output tsv | Out-String).Trim()
        $request = @{
            Method             = $Method
            Uri                = "$ProjectEndpoint$Path"
            Headers            = @{ Authorization = "Bearer $token" }
            TimeoutSec         = 120
            SkipHttpErrorCheck = $true
        }
        if ($null -ne $Body) {
            $request.Body = $Body | ConvertTo-Json -Depth 20 -Compress
            $request.ContentType = 'application/json'
        }
        $response = Invoke-WebRequest @request
        if ($response.StatusCode -ne 404 -or $attempt -ge 6) { break }
        Start-Sleep -Seconds 20
    }
    $content = $response.Content ? ($response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue) : $null
    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
        $detail = $content.error.code ?? $content.error ?? 'no-detail'
        throw "Foundry $Method $Path answered $($response.StatusCode): $detail"
    }
    $content
}

function Get-Definition {
    param([string] $File)
    $json = & yq -o=json '.' $File
    if ($LASTEXITCODE -ne 0) { Write-Fail "yq could not read $File" }
    $json | Out-String | ConvertFrom-Json -Depth 20
}

# 1. Point each server's backend at its application. The example server has none.
# An Agent without tools (simulator-personas) has no server.
$servers = @($files | ForEach-Object {
        if (-not (Test-Path -Path $_ -PathType Leaf)) { Write-Fail "there is no definition file $_" }
        (Get-Definition $_).tools.server
    } | Where-Object { $_ } | Sort-Object -Unique)
$apim = (Invoke-Az resource list --subscription $subscription --resource-group $resourceGroup --resource-type Microsoft.ApiManagement/service `
        --query '[0].name' --output tsv | Out-String).Trim()
if (-not $apim) { Write-Fail "there is no API Management service in $resourceGroup" }

function Set-BackendAddress {
    param([string] $Server, [string] $App, [string] $Backend, [string] $Variable, [string] $Route = '/agent/v1')
    $base = Get-AppUrl $App -ResourceGroup $resourceGroup
    Set-AzdEnvironmentValue $Variable $base
    Invoke-AzRest -Method patch -Body @{ properties = @{ url = "$base$Route" } } `
        -Url "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers/Microsoft.ApiManagement/service/$apim/backends/${Backend}?api-version=2024-05-01" | Out-Null
    Write-Pass "the $Server server calls $App at $base$Route"
}
foreach ($server in $servers) {
    switch ($server) {
        'integration-operations' { Set-BackendAddress $server integration-console-web integration-console-agent-api ESTATE_INTEGRATION_CONSOLE_BASE_URL }
        'health-check' { Set-BackendAddress $server health-check-web health-check-agent-api ESTATE_HEALTH_CHECK_BASE_URL }
        'customer-service' { Set-BackendAddress $server tms-api tms-agent-api ESTATE_TMS_BASE_URL -Route '/v1/agent/customer-service' }
        'finance-matching' { Set-BackendAddress $server erp-api erp-agent-api ESTATE_ERP_BASE_URL -Route '/v1/agent/finance' }
        'finance-rejection-reason' { Set-BackendAddress $server erp-api erp-rejection-reason-agent-api ESTATE_ERP_BASE_URL -Route '/v1/agent/finance-rejection-reason' }
        'customer-service-reply' { Set-BackendAddress $server tms-api tms-reply-agent-api ESTATE_TMS_BASE_URL -Route '/v1/agent/customer-service-reply' }
        'planner-notification' { Set-BackendAddress $server tms-api tms-planner-notification-agent-api ESTATE_TMS_BASE_URL -Route '/v1/agent/planner-notification' }
        'planner' { Set-BackendAddress $server tms-api tms-planner-agent-api ESTATE_TMS_BASE_URL -Route '/v1/agent/planner' }
        default { }
    }
}

# The Booking draft host is the Intake agent's door, not an MCP server: the Partner API's booking-drafts operation calls it.
if ($files | Where-Object { (Split-Path -Leaf $_) -replace '\.agent\.yaml$' -eq 'booking-intake' }) {
    Set-BackendAddress 'booking-draft' booking-draft-api booking-draft-host ESTATE_BOOKING_DRAFT_BASE_URL -Route ''
}

# 2. The connections. An Agent without tools needs none.
if ($servers.Count -gt 0) {
    & (Join-Path $repo 'infra/create-agent-connection.ps1') @servers
}

# 3 and 4. Publish.
$account = (Invoke-Az cognitiveservices account list --subscription $subscription --resource-group $resourceGroup `
        --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$deployments = @(Invoke-Az cognitiveservices account deployment list --subscription $subscription --resource-group $resourceGroup `
        --name $account --query '[].name' --output json | Out-String | ConvertFrom-Json)
New-Item -ItemType Directory -Path (Split-Path -Parent $published) -Force | Out-Null
$results = @()
foreach ($file in $files) {
    $definition = Get-Definition $file
    $agentName = $definition.name
    if ((Split-Path -Leaf $file) -ne "$agentName.agent.yaml") { Write-Fail "$file names the Agent '$agentName'" }
    $server = [string]$definition.tools.server
    $header = [string]$definition.inputs.correlationIdHeader
    $deployment = $definition.model.deployment
    if ($deployment -notin $deployments) {
        $deployment = $deployments | Select-Object -First 1
        if (-not $deployment) { Write-Fail 'the Foundry account has no model deployment (foundryModelDeployed is off?)' }
        Write-Host "NOTE  ${agentName}: its model deployment is not in the project; using the estate's $deployment"
    }

    if ($server) {
        $body = [ordered]@{
            description = $definition.description
            metadata    = @{ definitionVersion = [string]$definition.version }
            definition  = [ordered]@{
                kind              = 'prompt'
                model             = $deployment
                instructions      = $definition.instructions
                temperature       = $definition.model.temperature
                tools             = @([ordered]@{
                        type                  = 'mcp'
                        server_label          = $server -replace '-', '_'
                        server_url            = "$gateway/agents/$server/mcp"
                        require_approval      = 'never'
                        project_connection_id = "$server-mcp"
                        allowed_tools         = @($definition.tools.names)
                        headers               = @{ $header = '{{correlationId}}' }
                    })
                structured_inputs = @{
                    correlationId = [ordered]@{
                        description = 'The correlation ID of the Message trail in question, sent on every tool call.'
                        required    = $true
                        schema      = @{ type = 'string' }
                    }
                }
            }
        }
    }
    else {
        # Pure generation: no tool, no structured input.
        $body = [ordered]@{
            description = $definition.description
            metadata    = @{ definitionVersion = [string]$definition.version }
            definition  = [ordered]@{
                kind         = 'prompt'
                model        = $deployment
                instructions = $definition.instructions
                temperature  = $definition.model.temperature
                tools        = @()
            }
        }
    }
    $projectEndpoint = $agentName -in $intakeAgents ? $intakeEndpoint : $endpoint
    if (-not $projectEndpoint) { Write-Fail "${agentName}: no Foundry project endpoint for it (FOUNDRY_INTAKE_PROJECT_ENDPOINT is the Intake project's)" }
    $version = (Invoke-Foundry -Method POST -Path "/agents/$agentName/versions?api-version=$apiVersion" -Body $body -ProjectEndpoint $projectEndpoint).version
    if (-not $version) { Write-Fail "${agentName}: Foundry made no version" }
    Write-Pass "${agentName}: version $version (definition version $($definition.version)) is published"

    # An Agent with no tool reads nothing, so its identity needs no role.
    if (-not $server) { continue }

    # Foundry gives the Agent its own identity (the project's shared identity holds no role, so an Agent without one can call nothing).
    $agent = Invoke-Foundry -Method GET -Path "/agents/${agentName}?api-version=$apiVersion" -ProjectEndpoint $projectEndpoint
    $identity = $agent.instance_identity.principal_id ?? $agent.identity.principal_id
    if (-not $identity) { Write-Fail "${agentName}: Foundry gave the Agent no identity of its own" }
    $results += [ordered]@{ agent = $agentName; server = $server; agentIdentityId = $identity }
}
ConvertTo-Json -InputObject @($results) -Depth 5 | Set-Content -Path $published -Encoding utf8NoBOM

# 5. The roles.
& (Join-Path $repo 'infra/assign-agent-roles.ps1') $published
