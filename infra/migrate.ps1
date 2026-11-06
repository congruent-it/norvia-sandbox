#Requires -Version 7.4
<#
.SYNOPSIS
    azd postdeploy step: applies a database's migrations by starting the migrations Container Apps job the AppHost
    deploys for it, and fails the deployment if they fail (coding guidelines §5: migrations are a deployment step).
.EXAMPLE
    ./migrate.ps1 tms-migrations 'the TMS database'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][string] $Job,
    [Parameter(Mandatory, Position = 1)][string] $Database
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID

# A job can fail to start for reasons that pass, such as its managed identity not yet getting a token ("Failed to
# authenticate the user in Active Directory") or a serverless database still resuming. Migrations apply only what is
# pending, so a failed execution is started again.
for ($attempt = 1; $attempt -le 3; $attempt++) {
    $execution = (Invoke-Az containerapp job start --subscription $subscription --name $Job `
            --resource-group $resourceGroup --query name --output tsv | Out-String).Trim()
    Write-Host "Migrating $Database ($execution, attempt $attempt)..."

    # The job itself gives up after its replica timeout (30 minutes).
    do {
        $status = (Invoke-Az containerapp job execution show --subscription $subscription --name $Job `
                --resource-group $resourceGroup --job-execution-name $execution --query properties.status --output tsv | Out-String).Trim()
        $running = $status -in 'Running', 'Processing'
        if ($running) { Start-Sleep -Seconds 10 }
    } while ($running)

    if ($status -eq 'Succeeded') { break }
    Write-Warning "The migrations of $Database ended $status. See the $Job job's execution $execution in the portal."
    if ($attempt -ge 3) { Write-Fail "The migrations of $Database ended $status." }
    Start-Sleep -Seconds 30
}
Write-Host "$Database is up to date."
