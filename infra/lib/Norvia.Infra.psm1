#Requires -Version 7.4
<#
.SYNOPSIS
    The helpers the infra scripts share (ADR 0048): pass/fail, the app FQDN, the throwaway Container Apps job, Log
    Analytics queries and waiting.
.DESCRIPTION
    Import it from a script with
        Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force
    Every call to the Azure CLI goes through Invoke-Az, so a failing `az` stops the script with its own error and the
    tests mock one command.
#>

Set-StrictMode -Version Latest

function Write-Pass {
    <# .SYNOPSIS Prints "PASS  <message>". #>
    [Diagnostics.CodeAnalysis.SuppressMessage('PSAvoidUsingWriteHost', '', Justification = 'Scripts report to the console by design.')]
    param([Parameter(Mandatory, Position = 0)][string] $Message)
    Write-Host "PASS  $Message"
}

function Write-Fail {
    <# .SYNOPSIS Ends the script with a non-zero exit code and "FAIL  <message>". #>
    param([Parameter(Mandatory, Position = 0)][string] $Message)
    # A terminating error: `pwsh -File` exits 1, and a caller's finally block (the job cleanup) still runs.
    throw "FAIL  $Message"
}

function Invoke-Az {
    <#
    .SYNOPSIS Runs the Azure CLI and returns its output lines, failing when it exits non-zero.
    .PARAMETER Arguments Everything after `az`.
    .PARAMETER AllowFailure Returns $null instead of failing (the `|| true` of the bash scripts).
    #>
    param(
        [Parameter(Mandatory, ValueFromRemainingArguments)][string[]] $Arguments,
        [switch] $AllowFailure
    )
    $output = & az @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) { return $null }
        throw "az $($Arguments -join ' ') failed with exit code ${LASTEXITCODE}: $($output -join [Environment]::NewLine)"
    }
    # What `az` writes to stderr (warnings, such as Bicep's) is not part of its output: `--output json` stays parseable.
    $output | Where-Object { $_ -isnot [Management.Automation.ErrorRecord] }
}

function Import-AzdEnvironment {
    <#
    .SYNOPSIS Sets the selected azd environment's values (AZURE_ENV_NAME, AZURE_ESTATE_*, ...) as environment variables.
    .DESCRIPTION The PowerShell form of `eval "$(azd env get-values)"`.
    #>
    param([string[]] $Lines = (& azd env get-values))
    foreach ($line in $Lines) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
            $value = $Matches[2].Trim()
            if ($value.Length -ge 2 -and $value.StartsWith('"') -and $value.EndsWith('"')) {
                $value = $value.Substring(1, $value.Length - 2) -replace '\\(["\\])', '$1'
            }
            Set-Item -Path "Env:$($Matches[1])" -Value $value
        }
    }
}

function Get-ResourceGroupName {
    <# .SYNOPSIS AZURE_RESOURCE_GROUP, or rg-<AZURE_ENV_NAME> as azd names it. #>
    if ($env:AZURE_RESOURCE_GROUP) { return $env:AZURE_RESOURCE_GROUP }
    if (-not $env:AZURE_ENV_NAME) { Write-Fail 'neither AZURE_RESOURCE_GROUP nor AZURE_ENV_NAME is set; run Import-AzdEnvironment first' }
    "rg-$($env:AZURE_ENV_NAME)"
}

function Get-AppFqdn {
    <# .SYNOPSIS The ingress FQDN of a Container App, without a scheme. Fails when the app has none. #>
    param(
        [Parameter(Mandatory, Position = 0)][string] $Name,
        [string] $ResourceGroup = (Get-ResourceGroupName),
        [string] $Subscription = $env:AZURE_SUBSCRIPTION_ID
    )
    $scope = $Subscription ? @('--subscription', $Subscription) : @()
    $fqdn = (Invoke-Az containerapp show @scope --name $Name --resource-group $ResourceGroup `
            --query properties.configuration.ingress.fqdn --output tsv | Out-String).Trim()
    if (-not $fqdn) { Write-Fail "the Container App $Name has no ingress FQDN" }
    $fqdn
}

function Get-AppUrl {
    <# .SYNOPSIS https://<FQDN> of a Container App. #>
    param(
        [Parameter(Mandatory, Position = 0)][string] $Name,
        [string] $ResourceGroup = (Get-ResourceGroupName),
        [string] $Subscription = $env:AZURE_SUBSCRIPTION_ID
    )
    "https://$(Get-AppFqdn -Name $Name -ResourceGroup $ResourceGroup -Subscription $Subscription)"
}

function Wait-Until {
    <#
    .SYNOPSIS Runs a condition until it returns something, and returns that; $null after the last attempt.
    .DESCRIPTION The caller decides what running out means, usually Write-Fail. "Something" is any non-empty result
    (an empty string, an empty array and $null mean "not yet"), as `until_found` in the bash scripts.
    .PARAMETER Condition The script block to run.
    .PARAMETER Attempts How many times to run it. Defaults to 30 (10 minutes at 20 seconds).
    .PARAMETER IntervalSeconds How long to sleep between attempts. Defaults to 20.
    #>
    param(
        [Parameter(Mandatory, Position = 0)][scriptblock] $Condition,
        [ValidateRange(1, 10000)][int] $Attempts = 30,
        [ValidateRange(0, 3600)][int] $IntervalSeconds = 20
    )
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $result = & $Condition
        if ($null -ne $result -and -not ($result -is [string] -and [string]::IsNullOrWhiteSpace($result)) -and @($result).Count -gt 0) {
            return $result
        }
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $IntervalSeconds }
    }
    $null
}

function Get-LogAnalyticsWorkspaceId {
    <# .SYNOPSIS The customer ID of a Log Analytics workspace, which its query API wants. #>
    param(
        [Parameter(Mandatory)][string] $WorkspaceName,
        [string] $ResourceGroup = (Get-ResourceGroupName)
    )
    (Invoke-Az monitor log-analytics workspace show --resource-group $ResourceGroup `
            --workspace-name $WorkspaceName --query customerId --output tsv | Out-String).Trim()
}

function Invoke-LogAnalyticsQuery {
    <#
    .SYNOPSIS Runs a KQL query over the last day and returns its rows (arrays of column values).
    .PARAMETER Timespan ISO 8601 duration. Defaults to P1D.
    #>
    param(
        [Parameter(Mandatory)][string] $WorkspaceId,
        [Parameter(Mandatory, Position = 0)][string] $Query,
        [string] $Timespan = 'P1D'
    )
    # The body goes in a file: quoting JSON on a command line differs between shells and operating systems.
    $body = New-TemporaryFile
    try {
        @{ query = $Query; timespan = $Timespan } | ConvertTo-Json -Compress | Set-Content -Path $body -Encoding utf8NoBOM
        $response = Invoke-Az rest --method post --resource https://api.loganalytics.io `
            --url "https://api.loganalytics.io/v1/workspaces/$WorkspaceId/query" `
            --body "@$($body.FullName)" --output json | Out-String | ConvertFrom-Json
        , @($response.tables[0].rows)
    }
    finally {
        Remove-Item -Path $body -Force -ErrorAction SilentlyContinue
    }
}

function New-ThrowawayJob {
    <#
    .SYNOPSIS Creates a Container Apps job from a definition. Pair it with Remove-ThrowawayJob in a finally block.
    .PARAMETER Name Usually "smoke-<what>-<unix seconds>"; see Get-ThrowawayJobName.
    .PARAMETER Definition The job as a hashtable, the shape of the ARM resource (location, identity, properties).
    #>
    [Diagnostics.CodeAnalysis.SuppressMessage('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Scripts create throwaway jobs by design.')]
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][hashtable] $Definition,
        [string] $ResourceGroup = (Get-ResourceGroupName)
    )
    # A definition file: `az containerapp job create --args` takes a leading -c for its own flag.
    $file = New-TemporaryFile
    try {
        $Definition | ConvertTo-Json -Depth 20 | Set-Content -Path $file -Encoding utf8NoBOM
        Invoke-Az containerapp job create --name $Name --resource-group $ResourceGroup --yaml $file.FullName --output none | Out-Null
    }
    finally {
        Remove-Item -Path $file -Force -ErrorAction SilentlyContinue
    }
    $Name
}

function Get-ThrowawayJobName {
    <# .SYNOPSIS "<Prefix>-<unix seconds>", unique per run. #>
    param([Parameter(Mandatory, Position = 0)][string] $Prefix)
    "$Prefix-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
}

function Start-ThrowawayJob {
    <#
    .SYNOPSIS Starts a job, waits for its execution to finish and returns @{ Execution; Status }.
    .PARAMETER PollSeconds How long to sleep between status reads. Defaults to 10.
    #>
    [Diagnostics.CodeAnalysis.SuppressMessage('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Scripts start throwaway jobs by design.')]
    param(
        [Parameter(Mandatory)][string] $Name,
        [string] $ResourceGroup = (Get-ResourceGroupName),
        [int] $PollSeconds = 10
    )
    $execution = (Invoke-Az containerapp job start --name $Name --resource-group $ResourceGroup --query name --output tsv | Out-String).Trim()
    do {
        $status = (Invoke-Az containerapp job execution show --name $Name --resource-group $ResourceGroup `
                --job-execution-name $execution --query properties.status --output tsv | Out-String).Trim()
        $running = $status -in 'Running', 'Processing'
        if ($running) { Start-Sleep -Seconds $PollSeconds }
    } while ($running)
    [pscustomobject]@{ Execution = $execution; Status = $status }
}

function Remove-ThrowawayJob {
    <# .SYNOPSIS Deletes a job, ignoring a failure (it may never have been created). #>
    [Diagnostics.CodeAnalysis.SuppressMessage('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Cleanup runs unattended in a finally block.')]
    param(
        [Parameter(Mandatory)][string] $Name,
        [string] $ResourceGroup = (Get-ResourceGroupName)
    )
    Invoke-Az containerapp job delete --name $Name --resource-group $ResourceGroup --yes --output none -AllowFailure | Out-Null
}

function Get-RequiredEnvironmentVariable {
    <# .SYNOPSIS The value of an environment variable; fails with the message when it is not set (`${NAME:?message}` in bash). #>
    param(
        [Parameter(Mandatory, Position = 0)][string] $Name,
        [Parameter(Position = 1)][string] $Message = "$Name is not set"
    )
    $value = [Environment]::GetEnvironmentVariable($Name)
    if (-not $value) { Write-Fail $Message }
    $value
}

function Set-AzdEnvironmentValue {
    <# .SYNOPSIS `azd env set`, kept in the azd environment so deploying the estate again keeps the value. #>
    [Diagnostics.CodeAnalysis.SuppressMessage('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Scripts write the azd environment by design.')]
    param(
        [Parameter(Mandatory, Position = 0)][string] $Name,
        [Parameter(Mandatory, Position = 1)][string] $Value
    )
    & azd env set $Name $Value
    if ($LASTEXITCODE -ne 0) { Write-Fail "azd env set $Name failed with exit code $LASTEXITCODE" }
}

function Invoke-AzRest {
    <#
    .SYNOPSIS `az rest` with the body (a hashtable or object) in a file, returning the parsed JSON response, or $null when it has none. Headers are "Name=value" strings.
    .DESCRIPTION Quoting JSON on a command line differs between shells and operating systems, so the body goes in a file.
    #>
    param(
        [Parameter(Mandatory)][ValidateSet('get', 'post', 'patch', 'put', 'delete')][string] $Method,
        [Parameter(Mandatory)][string] $Url,
        $Body,
        [string[]] $Headers = @()
    )
    $file = $null
    try {
        $arguments = @('rest', '--method', $Method, '--url', $Url, '--output', 'json')
        if ($null -ne $Body) {
            $file = New-TemporaryFile
            $Body | ConvertTo-Json -Depth 20 -Compress | Set-Content -Path $file -Encoding utf8NoBOM
            $arguments += @('--headers', 'Content-Type=application/json') + $Headers + @('--body', "@$($file.FullName)")
        }
        elseif ($Headers.Count -gt 0) {
            $arguments += @('--headers') + $Headers
        }
        $text = (Invoke-Az @arguments | Out-String).Trim()
        if ($text) { $text | ConvertFrom-Json }
    }
    finally {
        if ($file) { Remove-Item -Path $file -Force -ErrorAction SilentlyContinue }
    }
}

function Publish-LogicApp {
    <#
    .SYNOPSIS Deploys a Logic App's workflows (and its local functions) to the Logic Apps Standard app the estate created.
    .DESCRIPTION Sets the app settings, builds the functions project when there is one (it writes into the Logic App
    project's lib/custom folder, ADR 0019), stages the project into a zip (infra/stage-logic-app.ps1) and deploys it.
    .PARAMETER LogicApp The app's name.
    .PARAMETER Project The Logic App project, relative to the repository root.
    .PARAMETER Include The files and folders of the project the zip holds.
    .PARAMETER Settings App settings to set, name to value. None leaves the app's settings alone.
    .PARAMETER FunctionsProject The local functions project to build first, relative to the repository root.
    .PARAMETER Title What the messages call the app, such as "the booking Logic App".
    .PARAMETER RetrySeconds How long to wait before deploying again. A changed app setting restarts the app, and a zip
    deployment during the restart fails; deploying again is safe.
    #>
    [Diagnostics.CodeAnalysis.SuppressMessage('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Scripts deploy by design.')]
    [Diagnostics.CodeAnalysis.SuppressMessage('PSAvoidUsingWriteHost', '', Justification = 'Scripts report to the console by design.')]
    param(
        [Parameter(Mandatory)][string] $LogicApp,
        [Parameter(Mandatory)][string] $Project,
        [Parameter(Mandatory)][string[]] $Include,
        [hashtable] $Settings = @{},
        [string] $FunctionsProject,
        [Parameter(Mandatory)][string] $Title,
        [string] $ResourceGroup = (Get-ResourceGroupName),
        [string] $Subscription = $env:AZURE_SUBSCRIPTION_ID,
        [int] $RetrySeconds = 60
    )
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $scope = $Subscription ? @('--subscription', $Subscription) : @()

    if ($Settings.Count -gt 0) {
        $pairs = @(foreach ($name in $Settings.Keys) { "$name=$($Settings[$name])" })
        Invoke-Az logicapp config appsettings set @scope --name $LogicApp --resource-group $ResourceGroup --settings @pairs --output none | Out-Null
    }

    if ($FunctionsProject) {
        & dotnet build (Join-Path $repo $FunctionsProject) --configuration Release
        if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet build of $FunctionsProject failed with exit code $LASTEXITCODE" }
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) "logic-app-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $zip = Join-Path $work "$(Split-Path -Leaf $Project).zip"
        & (Join-Path $repo 'infra/stage-logic-app.ps1') (Join-Path $repo $Project) $zip @Include

        for ($attempt = 1; $attempt -le 3; $attempt++) {
            Write-Host "Deploying $Title to $LogicApp (attempt $attempt)..."
            try {
                Invoke-Az logicapp deployment source config-zip @scope --name $LogicApp --resource-group $ResourceGroup `
                    --src $zip --output none | Out-Null
                break
            }
            catch {
                if ($attempt -ge 3) { throw }
                Start-Sleep -Seconds $RetrySeconds
            }
        }
    }
    finally {
        Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-AzAccessToken {
    <# .SYNOPSIS An access token of the signed-in Azure CLI identity for a resource. It is never printed. #>
    param([Parameter(Mandatory, Position = 0)][string] $Resource)
    (Invoke-Az account get-access-token --resource $Resource --query accessToken --output tsv | Out-String).Trim()
}

function Get-FoundryHeader {
    <# .SYNOPSIS The Authorization header for the Foundry project's API, as the signed-in Azure CLI identity. It is never printed. #>
    @{ Authorization = "Bearer $(Get-AzAccessToken https://ai.azure.com)" }
}

function Get-Graph {
    <# .SYNOPSIS A Microsoft Graph v1.0 read, as the signed-in Azure CLI identity. #>
    param([Parameter(Mandatory, Position = 0)][string] $Path)
    Invoke-AzRest -Method get -Url "https://graph.microsoft.com/v1.0$Path"
}

function Get-ThrowawayJobDefinition {
    <#
    .SYNOPSIS The definition of a Container Apps job that runs shell commands as a user-assigned identity in the estate's environment.
    .DESCRIPTION The commands run in the Azure CLI image, so they stay `sh` (with curl and python3 there) whatever the
    caller's shell is. The definition goes to New-ThrowawayJob, which writes it to a file.
    .PARAMETER Name The container's name, such as "smoke-rating".
    .PARAMETER Calls The shell script the container runs.
    .PARAMETER IdentityId The resource ID(s) of the user-assigned identity or identities the job runs as.
    .PARAMETER Environment The container's environment variables, name to value.
    .PARAMETER Secrets Variables whose value is a secret of the job, name to value.
    The secret is named after the variable in kebab-case, so a value never shows in the job's definition.
    .PARAMETER ReplicaTimeout Seconds the execution may run. Defaults to 300.
    #>
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Calls,
        [Parameter(Mandatory)][string[]] $IdentityId,
        [System.Collections.IDictionary] $Environment = @{},
        [System.Collections.IDictionary] $Secrets = @{},
        [int] $ReplicaTimeout = 300,
        [string] $Location = $env:AZURE_LOCATION,
        [string] $ResourceGroup = (Get-ResourceGroupName)
    )
    $environmentId = (Invoke-Az containerapp env list --resource-group $ResourceGroup --query '[0].id' --output tsv | Out-String).Trim()
    $identities = @{}
    foreach ($id in $IdentityId) { $identities[$id] = @{} }
    $configuration = @{
        triggerType         = 'Manual'
        replicaTimeout      = $ReplicaTimeout
        replicaRetryLimit   = 0
        manualTriggerConfig = @{ parallelism = 1; replicaCompletionCount = 1 }
    }
    if ($Secrets.Count -gt 0) {
        $configuration.secrets = @(foreach ($key in $Secrets.Keys) { @{ name = $key.ToLowerInvariant().Replace('_', '-'); value = [string] $Secrets[$key] } })
    }
    $variables = @(foreach ($key in $Environment.Keys) { @{ name = $key; value = [string] $Environment[$key] } }) +
    @(foreach ($key in $Secrets.Keys) { @{ name = $key; secretRef = $key.ToLowerInvariant().Replace('_', '-') } })
    @{
        location   = $Location
        identity   = @{ type = 'UserAssigned'; userAssignedIdentities = $identities }
        properties = @{
            environmentId = $environmentId
            configuration = $configuration
            template      = @{
                containers = @(@{
                        name      = $Name
                        image     = 'mcr.microsoft.com/azure-cli:latest'
                        command   = @('/bin/sh', '-c', ($Calls -replace "`r", ''))
                        env       = $variables
                        resources = @{ cpu = 0.5; memory = '1Gi' }
                    })
            }
        }
    }
}

function Get-ThrowawayJobResult {
    <#
    .SYNOPSIS The text after a marker on the first console log line of a job that starts with it, or $null when none has reached Log Analytics yet.
    .DESCRIPTION Wrap it in Wait-Until: ingestion takes a few minutes.
    .PARAMETER Marker What the line starts with. Defaults to "SMOKE-RESULT ".
    .PARAMETER Since Only lines logged after this time: a job started again must not be answered with its earlier result.
    #>
    param(
        [Parameter(Mandatory)][string] $WorkspaceId,
        [Parameter(Mandatory)][string] $JobName,
        [string] $Marker = 'SMOKE-RESULT ',
        [string] $Timespan = 'P1D',
        [Nullable[datetime]] $Since
    )
    $after = $Since ? " and TimeGenerated > datetime($($Since.ToUniversalTime().ToString('o')))" : ''
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $WorkspaceId -Timespan $Timespan `
        "ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$JobName' and Log_s startswith '$Marker'$after | order by TimeGenerated desc | project Log_s"
    if ($rows.Count -eq 0) { return $null }
    $rows[0][0].Substring($Marker.Length)
}

function Get-RandomSuffix {
    <# .SYNOPSIS A short random number, to keep names made in the same second apart. #>
    Get-Random -Minimum 0 -Maximum 32768
}

function Invoke-ThrowawayJobForResult {
    <#
    .SYNOPSIS Runs shell commands as an identity in a throwaway Container Apps job and returns the JSON they print after "SMOKE-RESULT ", parsed.
    .DESCRIPTION Creates the job, runs it, waits for it, reads its result from Log Analytics (ingestion takes a few minutes,
    so for up to 10 minutes) and deletes the job, however it ends. Fails when the execution does not succeed or the result
    never arrives. The commands print only what the checks need.
    .PARAMETER Prefix The job's name without its unix time; also the container's name, such as "smoke-rating".
    .PARAMETER Purpose What the execution did, for the failure message, such as "reading the TMS".
    #>
    param(
        [Parameter(Mandatory)][string] $Prefix,
        [Parameter(Mandatory)][string] $Calls,
        [Parameter(Mandatory)][string[]] $IdentityId,
        [System.Collections.IDictionary] $Environment = @{},
        [System.Collections.IDictionary] $Secrets = @{},
        [int] $ReplicaTimeout = 300,
        [string] $Purpose = 'the job',
        [string] $ResourceGroup = (Get-ResourceGroupName),
        [string] $WorkspaceName = $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE
    )
    $job = Get-ThrowawayJobName $Prefix
    try {
        $definition = Get-ThrowawayJobDefinition -Name $Prefix -Calls $Calls -IdentityId $IdentityId -Environment $Environment -Secrets $Secrets `
            -ReplicaTimeout $ReplicaTimeout -ResourceGroup $ResourceGroup
        New-ThrowawayJob -Name $job -Definition $definition -ResourceGroup $ResourceGroup | Out-Null
        $started = Start-ThrowawayJob -Name $job -ResourceGroup $ResourceGroup
        if ($started.Status -ne 'Succeeded') { Write-Fail "$Purpose ended $($started.Status); see the logs of job execution $($started.Execution)" }
        $workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $WorkspaceName -ResourceGroup $ResourceGroup
        $text = Wait-Until { Get-ThrowawayJobResult -WorkspaceId $workspace -JobName $job }
        if (-not $text) { Write-Fail "the job's results didn't reach Log Analytics in 10 minutes" }
        $text | ConvertFrom-Json
    }
    finally {
        Remove-ThrowawayJob -Name $job -ResourceGroup $ResourceGroup
    }
}

function Get-DeadLetters {
    <#
    .SYNOPSIS The messages on a Service Bus queue's dead-letter queue, as the signed-in user: each is locked and released again, never settled.
    .DESCRIPTION Needs the Azure Service Bus Data Receiver role. Returns objects with CorrelationId and Reason; the bodies are never read.
    #>
    param(
        [Parameter(Mandatory)][string] $Namespace,
        [Parameter(Mandatory)][string] $Queue
    )
    # $Queue is a queue's name, or "<topic>/subscriptions/<subscription>" for a subscription's dead-letter queue.
    $headers = @{ Authorization = "Bearer $(Get-AzAccessToken https://servicebus.azure.net)" }
    $base = "https://$Namespace.servicebus.windows.net"
    $locations = @()
    for ($i = 0; $i -lt 50; $i++) {
        $response = Invoke-WebRequest -Method Post -Uri "$base/$Queue/`$deadletterqueue/messages/head?timeout=5" -Headers $headers -SkipHttpErrorCheck
        if ($response.StatusCode -ne 201) { break }
        $locations += @($response.Headers['Location'])[0]
        [pscustomobject]@{
            CorrelationId = (@($response.Headers['CorrelationId'])[0] ?? '').Trim('"')
            Reason        = (@($response.Headers['DeadLetterReason'])[0] ?? '').Trim('"')
        }
    }
    foreach ($location in $locations) {
        Invoke-WebRequest -Method Put -Uri $location -Headers $headers -SkipHttpErrorCheck | Out-Null
    }
}

function Get-DeadLetterReason {
    <# .SYNOPSIS The dead-letter reason of the message with a correlation ID, or $null when it is not there (yet). #>
    param(
        [Parameter(Mandatory)][string] $Namespace,
        [Parameter(Mandatory)][string] $Queue,
        [Parameter(Mandatory)][string] $CorrelationId
    )
    $message = Get-DeadLetters -Namespace $Namespace -Queue $Queue | Where-Object CorrelationId -EQ $CorrelationId | Select-Object -First 1
    $message ? $message.Reason : $null
}

function Assert-InApplicationInsights {
    <#
    .SYNOPSIS Waits (up to 10 minutes) until a value is found in the telemetry of each given app, then passes; fails when one is missing.
    .PARAMETER What How the pass and fail messages name the value, such as "PU, by correlation ID X,".
    .PARAMETER Value The text to find.
    .PARAMETER Roles The apps (the end of the AppRoleName; Azure Monitor names a Container App after its environment too).
    .PARAMETER Filter A KQL predicate with {0} for the value. Defaults to a match anywhere in Properties.
    #>
    param(
        [Parameter(Mandatory)][string] $WorkspaceId,
        [Parameter(Mandatory)][string] $What,
        [Parameter(Mandatory)][string] $Value,
        [Parameter(Mandatory)][string[]] $Roles,
        [string] $Filter = "tostring(Properties) contains '{0}'"
    )
    $found = @()
    $missing = $Roles
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $rows = Invoke-LogAnalyticsQuery -WorkspaceId $WorkspaceId @"
union AppRequests, AppTraces, AppDependencies
| where $($Filter -f $Value)
| summarize by AppRoleName
"@
        $found = @($rows | ForEach-Object { $_[0] })
        $missing = @($Roles | Where-Object { $role = $_; -not ($found | Where-Object { $_ -like "*$role*" }) })
        if ($missing.Count -eq 0) {
            Write-Pass "$What is in Application Insights: $($found -join ' ')"
            return
        }
        if ($attempt -lt 30) { Start-Sleep -Seconds 20 }
    }
    Write-Fail "$What isn't in Application Insights for $($missing -join ' ') after 10 minutes"
}

function Get-PersonaClientId {
    <# .SYNOPSIS The client ID of a Persona (such as van-dijk-transport) from AZURE_ESTATE_PERSONA_CLIENT_IDS ("name=id;name=id"); $null when there is none. #>
    param([Parameter(Mandatory, Position = 0)][string] $Name)
    foreach ($pair in "$($env:AZURE_ESTATE_PERSONA_CLIENT_IDS)" -split ';') {
        $key, $value = $pair -split '=', 2
        if ($key -eq $Name) { return $value }
    }
}

function Read-Confirmation {
    <# .SYNOPSIS Asks the presenter a yes/no question about what they saw; true for y or Y. #>
    param([Parameter(Mandatory, Position = 0)][string] $Question)
    (Read-Host "$Question [y/n]") -match '^[Yy]'
}

function Wait-ForPresenter {
    <# .SYNOPSIS Stops and says what the presenter must do, then waits for Enter. #>
    [Diagnostics.CodeAnalysis.SuppressMessage('PSAvoidUsingWriteHost', '', Justification = 'Scripts report to the console by design.')]
    param([Parameter(Mandatory, Position = 0)][string] $Message)
    Write-Host ''
    Write-Host $Message
    Read-Host 'Press Enter once it is done... ' | Out-Null
}

function Get-ActionIdsReaching {
    <#
    .SYNOPSIS The correlation IDs of a Shipment's TMS actions of a flow that a role also logged: the action's trail runs on into the Logic App. $null when none has.
    #>
    param(
        [Parameter(Mandatory)][string] $WorkspaceId,
        [Parameter(Mandatory)][string] $Shipment,
        [Parameter(Mandatory)][string] $Role,
        [string] $Flow = 'exception-handling'
    )
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $WorkspaceId @"
let ids = AppTraces | where tostring(Properties.shipmentNumber) == '$Shipment' and tostring(Properties.flow) == '$Flow'
    and tostring(Properties.outcome) == 'Succeeded' and AppRoleName endswith 'tms-api' | distinct tostring(Properties.correlationId);
union AppRequests, AppTraces, AppDependencies
| where tostring(Properties.correlationId) in (ids) and AppRoleName =~ '$Role'
| distinct tostring(Properties.correlationId)
"@
    $rows.Count -gt 0 ? @($rows | ForEach-Object { $_[0] }) : $null
}

function Get-PublishedEvent {
    <#
    .SYNOPSIS The name of an event ("publish <Event>") published on a Message trail in the TMS, or $null when none has been yet.
    .DESCRIPTION The trail is that of a correlation ID, or those of a Shipment's succeeded TMS actions of a flow.
    #>
    param(
        [Parameter(Mandatory)][string] $WorkspaceId,
        [Parameter(Mandatory)][string] $EventName,
        [string] $CorrelationId,
        [string] $Shipment,
        [string] $Flow = 'exception-handling'
    )
    $ids = $CorrelationId ? "tostring(Properties.correlationId) == '$CorrelationId'" : "tostring(Properties.correlationId) in (ids)"
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $WorkspaceId @"
let ids = AppTraces | where tostring(Properties.shipmentNumber) == '$Shipment' and tostring(Properties.flow) == '$Flow'
    and tostring(Properties.outcome) == 'Succeeded' and AppRoleName endswith 'tms-api' | distinct tostring(Properties.correlationId);
let trail = AppTraces | where $ids and isnotempty(OperationId) | distinct OperationId;
union AppRequests, AppTraces, AppDependencies
| where OperationId in (trail) and AppRoleName endswith 'tms-api' and Name == 'publish $EventName'
| distinct Name
"@
    $rows.Count -gt 0 ? $rows[0][0] : $null
}

function Get-DocumentHops {
    <# .SYNOPSIS The tracked hops of a document (an invoice or Credit note number) in a role, as "<outcome> <failureCode>" lines; $null when there are none. #>
    param(
        [Parameter(Mandatory)][string] $WorkspaceId,
        [Parameter(Mandatory)][string] $Document,
        [Parameter(Mandatory)][string] $Role
    )
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $WorkspaceId @"
AppTraces
| where AppRoleName =~ '$Role' and tostring(Properties.documentNumber) == '$Document'
| project outcome = tostring(Properties.outcome), code = tostring(Properties.failureCode)
| distinct outcome, code
"@
    $rows.Count -gt 0 ? @($rows | ForEach-Object { "$($_[0]) $($_[1])".TrimEnd() }) : $null
}

function Wait-DocumentHop {
    <# .SYNOPSIS Waits (up to 10 minutes) for a hop of a document in a role with an outcome, and a failure code if one is given; true when it arrived. #>
    param(
        [Parameter(Mandatory)][string] $WorkspaceId,
        [Parameter(Mandatory)][string] $Document,
        [Parameter(Mandatory)][string] $Role,
        [Parameter(Mandatory)][string] $Outcome,
        [string] $Code = ''
    )
    $wanted = "$Outcome $Code".TrimEnd()
    [bool] (Wait-Until { Get-DocumentHops -WorkspaceId $WorkspaceId -Document $Document -Role $Role | Where-Object { $_ -eq $wanted } })
}

function Read-DocumentNumber {
    <# .SYNOPSIS Asks the presenter for an invoice number, and fails when it is not one. #>
    param([Parameter(Mandatory, Position = 0)][string] $Prompt)
    $answer = Read-Host $Prompt
    if ($answer -notmatch '^[A-Za-z0-9-]+$') { Write-Fail "that isn't an invoice number" }
    $answer
}

function Invoke-AsFound {
    <#
    .SYNOPSIS Sets the as-found toggles and deploys the estate with them (infra/as-found.ps1, docs/operations/as-found.md).
    .DESCRIPTION Fails when the script does. It prints what the script prints.
    #>
    param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]] $Arguments)
    & (Join-Path $PSScriptRoot '../as-found.ps1') @Arguments
    if ($LASTEXITCODE -ne 0) { Write-Fail "as-found.ps1 $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Get-MetricAlertValue {
    <# .SYNOPSIS One value of a metric alert rule (a JMESPath query), or $null when the rule does not exist. #>
    param(
        [Parameter(Mandatory, Position = 0)][string] $Name,
        [Parameter(Mandatory, Position = 1)][string] $Query,
        [string] $ResourceGroup = (Get-ResourceGroupName)
    )
    $output = Invoke-Az monitor metrics alert show --resource-group $ResourceGroup --name $Name --query $Query --output tsv -AllowFailure
    if ($null -eq $output) { return $null }
    ($output | Out-String).Trim()
}

function Get-QueueDeadLetterCount {
    <# .SYNOPSIS The number of messages on a Service Bus queue's dead-letter queue. #>
    param(
        [Parameter(Mandatory)][string] $Namespace,
        [Parameter(Mandatory)][string] $Queue,
        [string] $ResourceGroup = (Get-ResourceGroupName)
    )
    [int] (Invoke-Az servicebus queue show --resource-group $ResourceGroup --namespace-name $Namespace --name $Queue `
            --query countDetails.deadLetterMessageCount --output tsv | Out-String).Trim()
}

function Test-SameValues {
    <# .SYNOPSIS Whether two lists hold the same values in the same order; $null and an empty list are the same. #>
    param($Left, $Right)
    (@($Left) -join [char]0) -eq (@($Right) -join [char]0)
}

function Get-TrailSummary {
    <#
    .SYNOPSIS What one Message trail of an app holds in Application Insights: the flows, outcomes and publishes on it, and the values of the tracking contract's keys.
    .DESCRIPTION The tracking contract's keys (coding guidelines section 7) are logged in Properties. A publish is a hop of
    its own, in the outbox worker, continuing the trace of the call that raised it, so a trail is found by OperationId, as
    the Integration console does, not by Properties alone. Azure Monitor names an app after its Container Apps environment
    too: [<environment>]/tms-api, so the role is matched by its end.
    .PARAMETER Keys The Properties keys to collect, such as shipmentNumber and partyId.
    .OUTPUTS $null when the trail has no rows yet; otherwise an ordered hashtable of flows, outcomes, published and each key, as arrays.
    #>
    param(
        [Parameter(Mandatory)][string] $WorkspaceId,
        [Parameter(Mandatory)][string] $CorrelationId,
        [string[]] $Keys = @(),
        [string] $Role = 'tms-api'
    )
    $keyColumns = @(foreach ($key in $Keys) { "    $key = make_set_if(tostring(Properties.$key), isnotempty(tostring(Properties.$key)))," })
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $WorkspaceId @"
let trail = AppTraces | where tostring(Properties.correlationId) == '$CorrelationId' and isnotempty(OperationId) | distinct OperationId;
union AppRequests, AppTraces, AppDependencies
| where OperationId in (trail) and AppRoleName endswith '$Role'
| summarize rows = count(),
    flows = make_set_if(tostring(Properties.flow), isnotempty(tostring(Properties.flow))),
    outcomes = make_set_if(tostring(Properties.outcome), isnotempty(tostring(Properties.outcome))),
$($keyColumns -join [Environment]::NewLine)
    published = make_set_if(Name, isnotempty(Name) and Name startswith 'publish ')
"@
    if ($rows.Count -eq 0 -or [int] $rows[0][0] -le 0) { return $null }
    $row = $rows[0]
    $summary = [ordered]@{ correlationId = $CorrelationId }
    $names = @('rows', 'flows', 'outcomes') + $Keys + @('published')
    for ($i = 1; $i -lt $names.Count; $i++) { $summary[$names[$i]] = @(@($row[$i] | ConvertFrom-Json) | Sort-Object) }
    $summary
}

Export-ModuleMember -Function Write-Pass, Write-Fail, Invoke-Az, Import-AzdEnvironment, Get-ResourceGroupName,
Get-AppFqdn, Get-AppUrl, Wait-Until, Get-LogAnalyticsWorkspaceId, Invoke-LogAnalyticsQuery,
New-ThrowawayJob, Get-ThrowawayJobName, Start-ThrowawayJob, Remove-ThrowawayJob, Get-RequiredEnvironmentVariable,
Set-AzdEnvironmentValue, Invoke-AzRest, Publish-LogicApp, Get-AzAccessToken, Get-FoundryHeader, Get-Graph, Get-ThrowawayJobDefinition,
Get-ThrowawayJobResult, Get-RandomSuffix, Test-SameValues, Get-TrailSummary, Invoke-ThrowawayJobForResult, Get-DeadLetters, Get-DeadLetterReason, Assert-InApplicationInsights, Get-PersonaClientId, Wait-ForPresenter, Get-ActionIdsReaching, Get-PublishedEvent, Read-Confirmation, Get-DocumentHops, Wait-DocumentHop, Read-DocumentNumber, Invoke-AsFound,
Get-MetricAlertValue, Get-QueueDeadLetterCount
