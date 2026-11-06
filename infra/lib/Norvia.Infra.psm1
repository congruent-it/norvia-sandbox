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
    .SYNOPSIS `az rest` with the body (a hashtable or object) in a file, returning the parsed JSON response, or $null when it has none.
    .DESCRIPTION Quoting JSON on a command line differs between shells and operating systems, so the body goes in a file.
    #>
    param(
        [Parameter(Mandatory)][ValidateSet('get', 'post', 'patch', 'put', 'delete')][string] $Method,
        [Parameter(Mandatory)][string] $Url,
        $Body
    )
    $file = $null
    try {
        $arguments = @('rest', '--method', $Method, '--url', $Url, '--output', 'json')
        if ($null -ne $Body) {
            $file = New-TemporaryFile
            $Body | ConvertTo-Json -Depth 20 -Compress | Set-Content -Path $file -Encoding utf8NoBOM
            $arguments += @('--headers', 'Content-Type=application/json', '--body', "@$($file.FullName)")
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

Export-ModuleMember -Function Write-Pass, Write-Fail, Invoke-Az, Import-AzdEnvironment, Get-ResourceGroupName,
Get-AppFqdn, Get-AppUrl, Wait-Until, Get-LogAnalyticsWorkspaceId, Invoke-LogAnalyticsQuery,
New-ThrowawayJob, Get-ThrowawayJobName, Start-ThrowawayJob, Remove-ThrowawayJob, Get-RequiredEnvironmentVariable,
Set-AzdEnvironmentValue, Invoke-AzRest, Publish-LogicApp
