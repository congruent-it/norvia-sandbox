#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke test of Transport Order intake on the deployed TMS (not in CI).
.DESCRIPTION
    Run it after `azd up`, signed in to the Azure CLI as the presenter the estate was provisioned for. It checks that:
      1. the holder of the TMS intake role (the intake Logic App's managed identity) submits a Transport Order and reads
         it back, from a throwaway Container Apps job that runs as that identity;
      2. a caller without the role is refused, by Entra and by the TMS;
      3. the TransportOrderAccepted event is on the estate's transport-order-events topic, seen through a subscription of
         the test's own;
      4. the TMS's traces of the call are in Application Insights, found by correlation ID.
#>
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$namespace = $env:AZURE_ESTATE_SERVICE_BUS_NAMESPACE
$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
$correlationId = [guid]::NewGuid().ToString()
$shipperReference = "SMOKE-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"

$order = Get-Content -Raw (Join-Path $PSScriptRoot '../contracts/partner-api/samples/bookings/minimal.transport-order.json') | ConvertFrom-Json -AsHashtable
$day = [DateTime]::UtcNow.AddDays(2).ToString('yyyy-MM-dd')
$order.shipperReference = $shipperReference
$order.collection.window = [ordered]@{ earliest = "${day}T06:00:00+00:00"; latest = "${day}T10:00:00+00:00" }
$booking = $order | ConvertTo-Json -Depth 20 -Compress

Write-Host "TMS: $tms"
Write-Host "Correlation ID: $correlationId"

# The test's own subscription, created before the event is published, receives only this call's event. Its filter
# keeps to the tracking contract (coding guidelines §7). Service Bus deletes it after 10 idle minutes if the cleanup
# below doesn't run.
$run = Get-ThrowawayJobName 'smoke-intake'
$job = $run
$subscription = $run
$subscriptionArguments = @('--resource-group', $resourceGroup, '--namespace-name', $namespace, '--topic-name', 'transport-order-events')
try {
    Invoke-Az servicebus topic subscription create @subscriptionArguments --name $subscription --auto-delete-on-idle PT10M --output none | Out-Null
    Invoke-Az servicebus topic subscription rule create @subscriptionArguments --subscription-name $subscription --name this-call `
        --filter-sql-expression "(sys.CorrelationId = '$correlationId' AND resubmitTo IS NULL) OR resubmitTo = '$subscription'" --output none | Out-Null
    Invoke-Az servicebus topic subscription rule delete @subscriptionArguments --subscription-name $subscription --name '$Default' --output none | Out-Null

    # 1. The intake identity submits and reads back. Its token can only be had inside Azure, so a job runs the calls.
    $calls = @'
set -eu
az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
token=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
status=$(curl -sS -o /tmp/submitted.json -w "%{http_code}" -X POST "$TMS/v1/transport-orders" \
  -H "Authorization: Bearer $token" -H "Content-Type: application/json" -H "X-Correlation-ID: $CORRELATION_ID" \
  -d "$BOOKING")
echo "Submitted: $status $(cat /tmp/submitted.json)"
[ "$status" = 201 ]
number=$(python3 -c "import json; print(json.load(open(\"/tmp/submitted.json\"))[\"transportOrderNumber\"])")
echo "Read back: $(curl -fsS "$TMS/v1/transport-orders/$number" -H "Authorization: Bearer $token")"
'@
    $definition = Get-ThrowawayJobDefinition -Name smoke-intake -Calls $calls -IdentityId $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
        -ResourceGroup $resourceGroup -Environment ([ordered]@{
            TMS               = $tms
            TMS_API_CLIENT_ID = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
            INTAKE_CLIENT_ID  = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
            CORRELATION_ID    = $correlationId
            BOOKING           = $booking
        })
    New-ThrowawayJob -Name $job -Definition $definition -ResourceGroup $resourceGroup | Out-Null
    $started = Start-ThrowawayJob -Name $job -ResourceGroup $resourceGroup
    if ($started.Status -ne 'Succeeded') { Write-Fail "the intake identity's submission ended $($started.Status); see the logs of job execution $($started.Execution)" }
    Write-Pass 'the intake identity submitted a Transport Order and read it back'

    # 2. A caller without the role is refused: Entra won't issue it a token for the TMS, and the TMS refuses other tokens.
    if ($null -ne (Invoke-Az account get-access-token --resource $env:AZURE_ESTATE_TMS_API_CLIENT_ID --output none -AllowFailure)) {
        $user = (Invoke-Az account show --query user.name --output tsv | Out-String).Trim()
        Write-Fail "Entra issued a TMS token to $user, who doesn't hold the intake role"
    }
    Write-Pass 'Entra issues no TMS token to a caller without the intake role'
    foreach ($token in @('', (Get-AzAccessToken 'https://management.azure.com'))) {
        $headers = @{ 'X-Correlation-ID' = [guid]::NewGuid().ToString() }
        if ($token) { $headers.Authorization = "Bearer $token" }
        $status = (Invoke-WebRequest -Method Post -Uri "$tms/v1/transport-orders" -Headers $headers -ContentType 'application/json' -Body $booking -SkipHttpErrorCheck).StatusCode
        if ($status -notin 401, 403) { Write-Fail "the TMS answered $status to a caller without the intake role" }
    }
    Write-Pass 'the TMS refuses a caller without a token and one with a token for another audience'

    # 3. The event is on the topic: receive it from the test's own subscription.
    $serviceBusToken = Get-AzAccessToken https://servicebus.azure.net
    $messages = "https://$namespace.servicebus.windows.net/transport-order-events/subscriptions/$subscription/messages/head?timeout=60"
    $response = Invoke-WebRequest -Method Delete -Uri $messages -Headers @{ Authorization = "Bearer $serviceBusToken" } -SkipHttpErrorCheck
    # The event type is the message's subject, which the REST API calls its label.
    $brokerProperties = @($response.Headers['BrokerProperties'])[0]
    $label = $brokerProperties ? ($brokerProperties | ConvertFrom-Json).Label : $null
    if ($label -ne 'TransportOrderAccepted') { Write-Fail "no TransportOrderAccepted event with correlation ID $correlationId reached transport-order-events" }
    Write-Pass 'TransportOrderAccepted is on the transport-order-events topic'

    # 4. The TMS's traces are in Application Insights. Ingestion takes a few minutes.
    $workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup
    # Azure Monitor names the TMS after its Container Apps environment too: [<environment>]/tms-api.
    $query = @"
let trail = AppTraces | where tostring(Properties.correlationId) == '$correlationId' and isnotempty(OperationId) | distinct OperationId;
union AppRequests, AppTraces, AppDependencies
| where OperationId in (trail) and AppRoleName endswith 'tms-api'
| summarize count() by Type
"@
    $rows = Wait-Until {
        $result = Invoke-LogAnalyticsQuery -WorkspaceId $workspace $query
        $types = @($result | ForEach-Object { $_[0] })
        ($types -contains 'AppRequests' -and $types -contains 'AppTraces') ? (ConvertTo-Json -Compress -InputObject @($result)) : $null
    }
    if (-not $rows) { Write-Fail "no TMS request and traces with correlation ID $correlationId in Application Insights after 10 minutes" }
    Write-Pass "the TMS's request and traces for the call are in Application Insights: $rows"
}
finally {
    Remove-ThrowawayJob -Name $job -ResourceGroup $resourceGroup
    Invoke-Az servicebus topic subscription delete @subscriptionArguments --name $subscription --output none -AllowFailure | Out-Null
}
