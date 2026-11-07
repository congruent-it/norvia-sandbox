#Requires -Version 7.4
<#
.SYNOPSIS
    azd preup and preprovision hook: deploys the estate Bicep (ADR 0004), then stores its outputs in the azd
    environment.
.DESCRIPTION
    azd looks up the AppHost's Estate parameters there when it provisions and deploys the apps. The hook runs before
    `azd up` too, because azd asks for those parameters before it runs the preprovision hook; `azd provision` on its own
    runs it as preprovision.

    It deploys with the Azure CLI, which must be signed in to the same tenant as azd, as a user who may create app
    registrations (the Entra ones are part of the estate).
#>
$ErrorActionPreference = 'Stop'
$PSNativeCommandArgumentPassing = 'Standard'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force
Set-Location $PSScriptRoot

if (-not $env:AZURE_SUBSCRIPTION_ID) { Write-Fail 'Choose a subscription first: azd env set AZURE_SUBSCRIPTION_ID <id>' }
if (-not $env:AZURE_LOCATION) { Write-Fail 'Choose a region first: azd env set AZURE_LOCATION <region>' }
$subscription = $env:AZURE_SUBSCRIPTION_ID
$envName = $env:AZURE_ENV_NAME
$resourceGroup = "rg-$envName"

function Get-AzJson {
    # The JSON an `az` command prints, parsed; $null when it printed nothing.
    $text = (Invoke-Az @args --output json | Out-String).Trim()
    if ($text) { $text | ConvertFrom-Json -Depth 20 }
}

function Test-AzSucceeds {
    # Whether an `az` command that prints one value (--query ... --output tsv) succeeds and finds it.
    [bool](Invoke-Az @args -AllowFailure)
}

function Invoke-AzOrWarn {
    # Best effort: an `az` command whose failure is only reported (the `|| echo "Could not ..." >&2` of the bash).
    param([Parameter(Mandatory)][string] $Warning, [Parameter(Mandatory, ValueFromRemainingArguments)][string[]] $AzArguments)
    try { Invoke-Az @AzArguments | Out-Null }
    catch { Write-Warning $Warning }
}

function Invoke-Azd {
    & azd @args
    if ($LASTEXITCODE -ne 0) { throw "azd $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

function Set-AzdValue {
    # An azd environment value, for provisioning, which finds an Estate parameter as AZURE_<NAME>.
    param([string] $Name, [string] $Value)
    Invoke-Azd env set $Name $Value
}

function Set-AzdParameter {
    # The environment's config value deploying reads, which parses what it is given as JSON and takes only strings.
    param([string] $Name, [string] $Value)
    Invoke-Azd env config set "infra.parameters.$Name" (ConvertTo-Json -InputObject $Value)
}

function Get-AzTsvLines {
    # The lines an `az ... --output tsv` command prints, none when it fails (the `2>/dev/null || true` of the bash).
    @(Invoke-Az @args --output tsv -AllowFailure) | ForEach-Object { "$_".Trim() } | Where-Object { $_ }
}

# In one `azd up` this runs twice, as preup and as preprovision, both times under the same azd process. The second
# time the estate is already deployed.
$azdProcessId = (Get-Process -Id $PID).Parent.Id
$deployedInThisRun = Join-Path ([IO.Path]::GetTempPath()) "norvia-estate-$envName-$azdProcessId"
if ((Test-Path $deployedInThisRun) -and ((Get-Item $deployedInThisRun).LastWriteTime -gt (Get-Date).AddMinutes(-180))) {
    Write-Host 'The Norvia estate is already deployed in this run of azd.'
    return
}

# Defender for Storage's malware scanning subscribes to the storage account's blob events through Event Grid. In a
# subscription that never registered Microsoft.EventGrid that subscription fails ("Microsoft.EventGrid is not
# registered"), and the account is left with on-upload scanning off although its deployment succeeded. Registering is
# idempotent, and --wait holds until it is done.
$eventGrid = Get-AzTsvLines provider show --subscription $subscription --namespace Microsoft.EventGrid --query registrationState
if ($eventGrid -ne 'Registered') {
    Write-Host 'Registering Microsoft.EventGrid, which malware scanning needs...'
    Invoke-Az provider register --subscription $subscription --namespace Microsoft.EventGrid --wait | Out-Null
}

$env:ESTATE_PRESENTER_PRINCIPAL_ID = [string](Get-AzTsvLines ad signed-in-user show --query id)

# The Carrier endpoints' credentials are derived from one random seed, made once. A new seed would change every
# credential the Partner portal has read from Key Vault, until it restarted.
if (-not $env:ESTATE_CARRIER_ENDPOINT_KEY_SEED) {
    $env:ESTATE_CARRIER_ENDPOINT_KEY_SEED = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    Invoke-Azd env set ESTATE_CARRIER_ENDPOINT_KEY_SEED $env:ESTATE_CARRIER_ENDPOINT_KEY_SEED
}

# Tearing the estate down soft-deletes its Key Vaults, and their names (derived from the resource group, so the same
# every time) stay taken until they are purged, which fails the next deployment with a ConflictError. A vault in
# deleted state holds nothing the estate still uses, so those of this environment's resource group are purged first. A
# vault with purge protection cannot be purged; it is reported and the deployment says so if it matters.
$resourceGroupId = "/subscriptions/$subscription/resourceGroups/$resourceGroup/"
$deletedVaults = Invoke-Az keyvault list-deleted --subscription $subscription --resource-type vault --output json -AllowFailure |
    Out-String | ConvertFrom-Json -ErrorAction SilentlyContinue
foreach ($vault in @($deletedVaults)) {
    if (-not $vault.properties.vaultId.StartsWith($resourceGroupId, [StringComparison]::OrdinalIgnoreCase)) { continue }
    if ($vault.properties.purgeProtectionEnabled) {
        Write-Warning "Key Vault $($vault.name) is deleted with purge protection; it cannot be purged."
        continue
    }
    Write-Host "Purging the deleted Key Vault $($vault.name)..."
    Invoke-AzOrWarn "Could not purge $($vault.name)." keyvault purge --subscription $subscription --name $vault.name --location $vault.properties.location --output none
}

# The same goes for API Management services and Foundry (Cognitive Services) accounts: their names (also derived from
# the resource group) stay taken while they are soft-deleted, which fails the deployment with
# ServiceAlreadyExistsInSoftDeletedState. Those of this environment's resource group are purged first.
$deletedApims = Invoke-Az apim deletedservice list --subscription $subscription --output json -AllowFailure |
    Out-String | ConvertFrom-Json -ErrorAction SilentlyContinue
foreach ($apim in @($deletedApims)) {
    $id = [string]$apim.serviceId
    if (-not ($id.StartsWith($resourceGroupId, [StringComparison]::OrdinalIgnoreCase) -or $id.StartsWith($resourceGroupId.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase))) { continue }
    Write-Host "Purging the deleted API Management service $($apim.name)..."
    Invoke-AzOrWarn "Could not purge $($apim.name)." apim deletedservice purge --subscription $subscription --service-name $apim.name --location $apim.location --output none
}

$deletedAccounts = Invoke-Az cognitiveservices account list-deleted --subscription $subscription --output json -AllowFailure |
    Out-String | ConvertFrom-Json -ErrorAction SilentlyContinue
foreach ($account in @($deletedAccounts)) {
    if (([string]$account.id) -notlike "*/resourceGroups/$resourceGroup/*") { continue }
    Write-Host "Purging the deleted Foundry account $($account.name)..."
    Invoke-AzOrWarn "Could not purge $($account.name)." cognitiveservices account purge --subscription $subscription `
        --resource-group $resourceGroup --name $account.name --location $account.location --output none
}

# Aspire's Azure SQL roles are granted by deployment scripts, each of which starts a container group. A script that
# failed (the region's container group quota, say) stays behind, and the next `azd provision` fails on it with
# "resource already exists ... pending delete". Its work is only SQL role grants, which run again, so the failed scripts
# of this resource group are deleted first.
$scriptsUrl = "https://management.azure.com${resourceGroupId}providers/Microsoft.Resources/deploymentScripts?api-version=2023-08-01"
$failedScripts = Get-AzTsvLines rest --method get --url $scriptsUrl --query "value[?properties.provisioningState=='Failed'].id"
foreach ($script in $failedScripts) {
    $scriptName = $script.Split('/')[-1]
    $scriptUrl = "https://management.azure.com${script}?api-version=2023-08-01"
    Write-Host "Deleting the failed deployment script $scriptName..."
    Invoke-AzOrWarn "Could not delete $scriptName." rest --method delete --url $scriptUrl --output none
    # The delete is asynchronous, and a script still being deleted fails the next deployment the same way, so wait
    # until it is gone.
    for ($i = 0; $i -lt 12; $i++) {
        if (-not (Test-AzSucceeds rest --method get --url $scriptUrl --query id --output tsv)) { break }
        Start-Sleep -Seconds 10
    }
}
# Even once it can no longer be read, ARM's identity service can keep the script as "pending delete" for a while
# longer, and a deployment that creates it again then fails with "Conflict ... pending delete". So after a delete, wait
# a little more before deploying.
if ($failedScripts) {
    Write-Host 'Waiting for the deleted deployment scripts to clear...'
    Start-Sleep -Seconds 120
}

# A new managed identity can take a minute to reach Microsoft Graph, which then refuses to grant it an app role.
# Deploying again is safe, so a first attempt that fails is retried.
$outputs = $null
for ($attempt = 1; $attempt -le 3; $attempt++) {
    Write-Host "Deploying the Norvia estate to $resourceGroup (attempt $attempt)..."
    try {
        $outputs = Get-AzJson deployment sub create --subscription $subscription --name "norvia-estate-$envName" `
            --location $env:AZURE_LOCATION --parameters estate.bicepparam --query properties.outputs
        break
    }
    catch {
        if ($attempt -ge 3) { throw }
        Write-Warning $_.Exception.Message
        Start-Sleep -Seconds 60
    }
}

# ARM changes the case of an output's name ("estatE_SERVICE_BUS_NAMESPACE"), so outputs are matched upper-cased.
function Get-EstateOutput {
    param([Parameter(Mandatory)][string] $Name)
    $outputs.PSObject.Properties | Where-Object { $_.Name.ToUpperInvariant() -eq $Name } | ForEach-Object { $_.Value.value }
}

# A new subscription starts with a $Default rule that passes every message, and the Bicep adds each subscription's real
# filter as the rule "resubmit-aware". (A rule named $Default with a SQL filter in the Bicep does not work: Service Bus
# accepts it, then the rule is gone and the subscription receives nothing.) Deploying again never deletes a resource, so
# the $Default rules are deleted here, or each subscription would keep passing everything. Deleting one that is already
# gone is harmless.
$serviceBus = Get-EstateOutput 'ESTATE_SERVICE_BUS_NAMESPACE'
if (-not $serviceBus) { Write-Fail 'The estate has no Service Bus namespace output.' }
foreach ($topic in Get-AzTsvLines servicebus topic list --subscription $subscription --resource-group $resourceGroup --namespace-name $serviceBus --query '[].name') {
    $subscriptions = Get-AzTsvLines servicebus topic subscription list --subscription $subscription --resource-group $resourceGroup `
        --namespace-name $serviceBus --topic-name $topic --query '[].name'
    foreach ($topicSubscription in $subscriptions) {
        $ruleArguments = @('--subscription', $subscription, '--resource-group', $resourceGroup, '--namespace-name', $serviceBus,
            '--topic-name', $topic, '--subscription-name', $topicSubscription, '--name', '$Default')
        if (Test-AzSucceeds servicebus topic subscription rule show @ruleArguments --query name --output tsv) {
            Write-Host "Deleting the `$Default rule of $topic/$topicSubscription..."
            Invoke-Az servicebus topic subscription rule delete @ruleArguments --output none | Out-Null
        }
    }
}

# Deploying again never deletes a resource, so an alert the as-found toggles now leave out is deleted here: a "no alert"
# is an absent rule, never a disabled one. The names are those infra/estate/alerts.bicep gives its rules.
if ($env:ESTATE_ASFOUND) {
    $asFound = $env:ESTATE_ASFOUND | ConvertFrom-Json
    $rules = @(@($asFound.flowsWithoutFailedRunAlert) | Where-Object { $_ } | ForEach-Object { "alert-failed-runs-$_" }) +
        @(@($asFound.entitiesWithoutDeadLetterAlert) | Where-Object { $_ } | ForEach-Object { "alert-dead-letter-$_" })
    foreach ($rule in $rules) {
        if (Test-AzSucceeds monitor metrics alert show --subscription $subscription --resource-group $resourceGroup --name $rule --query name --output tsv) {
            Write-Host "Deleting the alert $rule (as-found)..."
            Invoke-Az monitor metrics alert delete --subscription $subscription --resource-group $resourceGroup --name $rule | Out-Null
        }
    }

    # The same for the storage settings the toggles leave out. The estate's accounts are named stinbound<token> and
    # stdriver<token>; the TMS's is Aspire's (tagged aspire-resource-name=storage), and may not exist yet on the first
    # deployment.
    function Test-Toggle { param([string] $Name) $asFound.$Name -eq $true }
    function Get-StorageAccount {
        param([string] $Query)
        [string](Get-AzTsvLines storage account list --subscription $subscription --resource-group $resourceGroup --query $Query)
    }
    function Remove-LifecycleRule {
        param([string] $Account)
        if (-not $Account) { return }
        if (Test-AzSucceeds storage account management-policy show --subscription $subscription --resource-group $resourceGroup --account-name $Account --query name --output tsv) {
            Write-Host "Deleting the lifecycle rule of $Account (as-found)..."
            Invoke-Az storage account management-policy delete --subscription $subscription --resource-group $resourceGroup --account-name $Account | Out-Null
        }
    }
    if (Test-Toggle noInboundLifecycleRule) { Remove-LifecycleRule (Get-StorageAccount "[?starts_with(name, 'stinbound')].name | [0]") }
    if (Test-Toggle noTransitLifecycleRule) { Remove-LifecycleRule (Get-StorageAccount "[?starts_with(name, 'stdriver')].name | [0]") }
    if (Test-Toggle proofFilesStayHot) { Remove-LifecycleRule (Get-StorageAccount '[?tags."aspire-resource-name" == ''storage''].name | [0]') }
    if (Test-Toggle noMalwareScanning) {
        $inboundId = Get-StorageAccount "[?starts_with(name, 'stinbound')].id | [0]"
        if ($inboundId) {
            # The setting cannot be deleted ("DeleteNotSupported"), so "absent" is the setting turned off, for this
            # account whatever the subscription's own Defender plan says. It is only written when scanning is on, and
            # the estate Bicep turns it on again when the toggle is cleared.
            $defenderUrl = "https://management.azure.com$inboundId/providers/Microsoft.Security/defenderForStorageSettings/current?api-version=2022-12-01-preview"
            $scanning = Get-AzTsvLines rest --method get --url $defenderUrl --query 'properties.malwareScanning.onUpload.isEnabled || properties.isEnabled'
            if ($scanning -ieq 'true') {
                Write-Host 'Turning off Defender for Storage malware scanning on the inbound account (as-found)...'
                $body = ConvertTo-Json -Compress -Depth 5 -InputObject @{
                    properties = @{
                        isEnabled                         = $false
                        overrideSubscriptionLevelSettings = $true
                        malwareScanning                   = @{ onUpload = @{ isEnabled = $false } }
                        sensitiveDataDiscovery            = @{ isEnabled = $false }
                    }
                }
                Invoke-AzOrWarn 'Could not turn off malware scanning.' rest --method put --url $defenderUrl --output none --body $body
            }
        }
    }
}

# Each output as its name and its value as it is (a string is kept as it is; anything else is its JSON). Provisioning
# finds an Estate parameter as AZURE_<NAME> in the azd environment, but deploying, which puts some of them straight into
# the apps' settings, reads them only from the environment's config as infra.parameters.<name>. That config parses what
# it is given as JSON, and deploying accepts only strings, so a number such as the retention goes in quoted.
foreach ($output in $outputs.PSObject.Properties) {
    $name = $output.Name.ToUpperInvariant()
    $value = $output.Value.value
    $text = switch ($value) {
        { $_ -is [string] } { $_ }
        { $_ -is [bool] } { $_ ? 'true' : 'false' }
        default { ConvertTo-Json -Compress -Depth 20 -InputObject $_ }
    }
    Set-AzdValue "AZURE_$name" $text
    Set-AzdParameter $name.ToLowerInvariant() $text
}

# Whether the TMS API, the Partner portal, the Driver app API and the ERP API take the Simulator's Reset estate
# (Reset:Enabled), which deletes every transactional record in them. Off unless the environment says so with
# ESTATE_RESET_ENABLED_SETTING=true. The Estate has 64 outputs, the most a template may have, so the setting is handed
# to the apps here and not as an output.
$resetEnabled = ("$env:ESTATE_RESET_ENABLED_SETTING".ToLowerInvariant() -eq 'true') ? 'true' : 'false'
Set-AzdValue AZURE_ESTATE_RESET_ENABLED $resetEnabled
Set-AzdParameter estate_reset_enabled $resetEnabled

# Whether the Agents do anything (Agents:Enabled), and the Foundry project endpoint the apps and scripts call. Off
# unless the environment says so with ESTATE_AGENTS_ENABLED_SETTING=true. Like the reset setting, both are handed to the
# apps here, because the Estate is at its 64 outputs. The account is the estate's only AIServices account; the projects
# are norvia and norvia-intake, the Intake agent's (infra/estate/foundry.bicep).
$agentsEnabled = ("$env:ESTATE_AGENTS_ENABLED_SETTING".ToLowerInvariant() -eq 'true') ? 'true' : 'false'
$foundryAccount = [string](Get-AzTsvLines cognitiveservices account list --subscription $subscription --resource-group $resourceGroup `
        --query "[?kind=='AIServices'].name | [0]")
if (-not $foundryAccount) { Write-Fail 'The estate has no Foundry account' }
$foundryProjectEndpoint = "https://$foundryAccount.services.ai.azure.com/api/projects/norvia"
$foundryIntakeProjectEndpoint = "https://$foundryAccount.services.ai.azure.com/api/projects/norvia-intake"
Set-AzdValue AZURE_ESTATE_AGENTS_ENABLED $agentsEnabled
Set-AzdParameter estate_agents_enabled $agentsEnabled
Set-AzdValue AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT $foundryProjectEndpoint
Set-AzdParameter estate_foundry_project_endpoint $foundryProjectEndpoint
Set-AzdValue FOUNDRY_PROJECT_ENDPOINT $foundryProjectEndpoint
Set-AzdValue FOUNDRY_INTAKE_PROJECT_ENDPOINT $foundryIntakeProjectEndpoint

# The Integration console's agent API: the app registration API Management's identity calls it as, and that identity.
# The estate is at its 64 outputs, so they are found by name (infra/estate/integration-operations-mcp.bicep).
$consoleAgentApiClientId = [string](Get-AzTsvLines ad app list --filter "displayName eq 'Norvia Integration console agent API ($envName)'" --query '[0].appId')
if (-not $consoleAgentApiClientId) { Write-Fail 'The estate has no Integration console agent API app registration' }
$apimClientId = [string](Get-AzTsvLines identity list --subscription $subscription --resource-group $resourceGroup `
        --query "[?starts_with(name, 'id-apim-')].clientId | [0]")
if (-not $apimClientId) { Write-Fail 'The estate has no API Management identity' }
Set-AzdValue AZURE_ESTATE_INTEGRATION_CONSOLE_AGENT_API_CLIENT_ID $consoleAgentApiClientId
Set-AzdParameter estate_integration_console_agent_api_client_id $consoleAgentApiClientId
Set-AzdValue AZURE_ESTATE_API_MANAGEMENT_CLIENT_ID $apimClientId
Set-AzdParameter estate_api_management_client_id $apimClientId

# The TMS API's agent API: the app registration API Management's identity calls the Customer service agent's view as
# (infra/estate/customer-service-mcp.bicep). Found by name, as the estate is at its 64 outputs.
$tmsAgentApiClientId = [string](Get-AzTsvLines ad app list --filter "displayName eq 'Norvia TMS agent API ($envName)'" --query '[0].appId')
if (-not $tmsAgentApiClientId) { Write-Fail 'The estate has no TMS agent API app registration' }
Set-AzdValue AZURE_ESTATE_TMS_AGENT_API_CLIENT_ID $tmsAgentApiClientId
Set-AzdParameter estate_tms_agent_api_client_id $tmsAgentApiClientId

# The ERP API's agent API: the app registration API Management's identity calls the Carrier invoice matching agent's view as
# (infra/estate/finance-matching-mcp.bicep). Found by name, as the estate is at its 64 outputs.
$erpAgentApiClientId = [string](Get-AzTsvLines ad app list --filter "displayName eq 'Norvia ERP agent API ($envName)'" --query '[0].appId')
if (-not $erpAgentApiClientId) { Write-Fail 'The estate has no ERP agent API app registration' }
Set-AzdValue AZURE_ESTATE_ERP_AGENT_API_CLIENT_ID $erpAgentApiClientId
Set-AzdParameter estate_erp_agent_api_client_id $erpAgentApiClientId

# The Estate health check: its app registration (the one Easy Auth signs operators in to), its agent API's app
# registration (the audience of the tokens API Management sends it) and its identity (Reader on the resource group). The
# estate is at its 64 outputs, so they are found by name (infra/estate/health-check.bicep,
# infra/estate/health-check-mcp.bicep).
$healthCheckClientId = [string](Get-AzTsvLines ad app list --filter "displayName eq 'Norvia Estate health check ($envName)'" --query '[0].appId')
if (-not $healthCheckClientId) { Write-Fail 'The estate has no Estate health check app registration' }
$healthCheckAgentApiClientId = [string](Get-AzTsvLines ad app list --filter "displayName eq 'Norvia Estate health check agent API ($envName)'" --query '[0].appId')
if (-not $healthCheckAgentApiClientId) { Write-Fail 'The estate has no Estate health check agent API app registration' }
$healthCheckIdentityName = [string](Get-AzTsvLines identity list --subscription $subscription --resource-group $resourceGroup `
        --query "[?starts_with(name, 'id-health-check-')].name | [0]")
if (-not $healthCheckIdentityName) { Write-Fail 'The estate has no Estate health check identity' }
Set-AzdValue AZURE_ESTATE_HEALTH_CHECK_CLIENT_ID $healthCheckClientId
Set-AzdParameter estate_health_check_client_id $healthCheckClientId
Set-AzdValue AZURE_ESTATE_HEALTH_CHECK_AGENT_API_CLIENT_ID $healthCheckAgentApiClientId
Set-AzdParameter estate_health_check_agent_api_client_id $healthCheckAgentApiClientId
Set-AzdValue AZURE_ESTATE_HEALTH_CHECK_IDENTITY_NAME $healthCheckIdentityName
Set-AzdParameter estate_health_check_identity_name $healthCheckIdentityName

# The seeded Drivers' Entra test users, whose domain and password the apps take as the Estate parameters azd asks for.
$driverAppRegistrationClientId = Get-EstateOutput 'ESTATE_DRIVER_APP_REGISTRATION_CLIENT_ID'
if (-not $driverAppRegistrationClientId) { Write-Fail 'The estate has no ESTATE_DRIVER_APP_REGISTRATION_CLIENT_ID output' }
& (Join-Path $PSScriptRoot 'create-driver-test-users.ps1') $driverAppRegistrationClientId
New-Item -ItemType File -Path $deployedInThisRun -Force | Out-Null
(Get-Item $deployedInThisRun).LastWriteTime = Get-Date
