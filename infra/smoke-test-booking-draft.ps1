#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy smoke run of the Intake agent's Booking draft in Azure (#459, not in CI).
.DESCRIPTION
    Post-deploy smoke run of the Intake agent's Booking draft in Azure (#459, not in CI). Run it in a terminal, signed in to the Azure CLI as
    the presenter the estate was provisioned for, once `azd up` has deployed the estate and the apps and infra/deploy-agents.ps1 has published
    the Intake agent (it is the last postdeploy hook), with ESTATE_AGENTS_ENABLED_SETTING=true so the operation is on:

      infra/smoke-test-booking-draft.ps1

    A Persona's token can be had only as the Partner portal's identity (ADR 0005), so a throwaway Container Apps job sends a synthetic email
    through API Management as Brouwer Dranken, a Shipper Persona, and as Van Dijk Transport, a Carrier Persona, and checks that:
      1. the real model answers Brouwer Dranken with a Booking draft: the Shipper reference the email gives, with the quote it came from;
      2. Van Dijk Transport, which does not hold TransportOrders.Draft, gets 403, and the host never sees the call;
      3. a text over the size cap is refused with 413 at the edge;
      4. only API Management can call the host: a call straight to it without a token, and with the Persona's token, is refused with 401;
      5. the hop is on the Message trail by correlation ID, in API Management, in the host and as the Agent run with the Agent's identity;
      6. no part of the email (two sentinels the script made up) is in Application Insights: not in AppTraces, AppRequests, AppDependencies,
         AppEvents or AppExceptions, which are the tables the Message trails read;
      7. the Intake agent's identity holds no role, the project's shared identity holds none, and the host's identity holds Foundry User on
         the Intake project and nothing else;
      8. the Intake project keeps no response of the run (the host asks with store: false and deletes before it returns).
    It prints names, ids and counts only: never a token, a key, the email or the draft.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$subscription = $env:AZURE_SUBSCRIPTION_ID
$arm = "https://management.azure.com/subscriptions/$subscription/resourceGroups/$resourceGroup/providers"
$partnerApi = $env:AZURE_ESTATE_PARTNER_API_URL
$intakeEndpoint = $env:FOUNDRY_INTAKE_PROJECT_ENDPOINT ? $env:FOUNDRY_INTAKE_PROJECT_ENDPOINT : $env:AZURE_ESTATE_FOUNDRY_INTAKE_PROJECT_ENDPOINT
$agent = 'booking-intake'
$foundryUserRoleId = '53ca6127-db72-4b80-b1b0-d745d6d5456d'
$tenant = (Invoke-Az account show --query tenantId --output tsv | Out-String).Trim()

if (-not $intakeEndpoint) { Write-Fail 'no Foundry Intake project endpoint: has the estate been deployed (azd provision)?' }
if (-not $partnerApi) { Write-Fail 'no Partner API address: has the estate been deployed (azd provision)?' }
$hostUrl = Get-AppUrl booking-draft-api -ResourceGroup $resourceGroup
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup
function Get-SubscriptionKey([string] $Name) {
    (Invoke-Az rest --method post --url "$arm/Microsoft.ApiManagement/service/$($env:AZURE_ESTATE_API_MANAGEMENT)/subscriptions/$Name/listSecrets?api-version=2024-05-01" `
            --query primaryKey --output tsv | Out-String).Trim()
}
$started = [DateTime]::UtcNow

# The synthetic email. The two sentinels are made up for this run; neither may be found in Application Insights afterwards.
$suffix = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$reference = "SMOKE-DRAFT-$suffix"
$consignee = "Smoke Consignee $suffix"
$text = "Hi Norvia,`n`nPlease collect 2 pallets (850 kg in total, 120x80x150 cm each) at Havenstraat 12, 3011 AB Rotterdam, Netherlands on 14-11-2030 between 08:00 and 12:00.`n" +
"Deliver to $consignee, Lindenallee 5, 50667 Koeln, Germany. Contact: Anna Mueller, +49 221 555 0100.`nOur reference is $reference.`n`nThanks,`nJo"
$body = @{ text = $text } | ConvertTo-Json -Compress

Write-Host "Norvia Partner API: $partnerApi"
Write-Host "Booking draft host: $hostUrl"

# 1 to 4. The calls, as the Partner portal's identity for the Personas and straight at the host.
$calls = @'
set -eu
az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
persona_token() {
  curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
    -d grant_type=client_credentials -d client_id="$1" -d scope="$PARTNER_API_CLIENT_ID/.default" \
    -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
    | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])"
}
draft() {
  curl -sS -o "/tmp/$1.json" -D "/tmp/$1.headers" -w "%{http_code}" -X POST "$PARTNER_API/booking-drafts" \
    -H "Authorization: Bearer $2" -H "Ocp-Apim-Subscription-Key: $3" -H "Content-Type: application/json" \
    -H "X-Shipper-ID: SHP-0003" -H "X-Correlation-ID: sent-by-the-caller" --data-binary "@$4" > "/tmp/$1.status"
}
printf '%s' "$BODY" > /tmp/body.json
python3 -c "print('{\"text\": \"' + 'x' * 41000 + '\"}')" > /tmp/large.json
brouwer=$(persona_token "$BROUWER_CLIENT_ID")
vandijk=$(persona_token "$VAN_DIJK_CLIENT_ID")
draft accepted "$brouwer" "$BROUWER_KEY" /tmp/body.json
draft refused "$vandijk" "$VAN_DIJK_KEY" /tmp/body.json
draft toolarge "$brouwer" "$BROUWER_KEY" /tmp/large.json
curl -sS -o /dev/null -w "%{http_code}" -X POST "$HOST/draft/v1/booking-drafts" -H "Content-Type: application/json" --data-binary @/tmp/body.json > /tmp/direct-none.status || echo 0 > /tmp/direct-none.status
curl -sS -o /dev/null -w "%{http_code}" -X POST "$HOST/draft/v1/booking-drafts" -H "Authorization: Bearer $brouwer" -H "Content-Type: application/json" --data-binary @/tmp/body.json > /tmp/direct-persona.status || echo 0 > /tmp/direct-persona.status
python3 - <<"PYTHON"
import json

def call(name):
    headers = {}
    for line in open(f"/tmp/{name}.headers"):
        key, _, value = line.partition(":")
        headers[key.strip().lower()] = value.strip()
    try:
        body = json.load(open(f"/tmp/{name}.json"))
    except ValueError:
        body = None
    return {"status": int(open(f"/tmp/{name}.status").read()), "correlationId": headers.get("x-correlation-id"), "body": body}

accepted = call("accepted")
body = accepted["body"] if isinstance(accepted["body"], dict) else {}
# Only what the checks need: the draft holds the Consignee's personal data, so its values are not printed.
summary = {
    "bookingRef": (body.get("booking") or {}).get("bookingRef"),
    "bookingRefQuote": (body.get("quotes") or {}).get("bookingRef"),
    "keys": sorted(body.keys()),
    "failureCode": body.get("failureCode"),
}
refused = call("refused")
toolarge = call("toolarge")
print("SMOKE-RESULT " + json.dumps({
    "accepted": {"status": accepted["status"], "correlationId": accepted["correlationId"], "draft": summary},
    "refused": {"status": refused["status"], "correlationId": refused["correlationId"]},
    "toolarge": {"status": toolarge["status"], "correlationId": toolarge["correlationId"]},
    "directWithoutToken": int(open("/tmp/direct-none.status").read()),
    "directWithPersonaToken": int(open("/tmp/direct-persona.status").read()),
}))
PYTHON
'@
$result = Invoke-ThrowawayJobForResult -Prefix smoke-booking-draft -Calls $calls -ResourceGroup $resourceGroup -ReplicaTimeout 600 `
    -Purpose "the Personas' calls" `
    -IdentityId $env:AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID `
    -Secrets ([ordered]@{ BROUWER_KEY = (Get-SubscriptionKey brouwer-dranken); VAN_DIJK_KEY = (Get-SubscriptionKey van-dijk-transport); BODY = $body }) -Environment ([ordered]@{
        TENANT                   = $tenant
        PARTNER_API              = $partnerApi
        HOST                     = $hostUrl
        PARTNER_API_CLIENT_ID    = $env:AZURE_ESTATE_PARTNER_API_CLIENT_ID
        PARTNER_PORTAL_CLIENT_ID = $env:AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID
        BROUWER_CLIENT_ID        = Get-PersonaClientId brouwer-dranken
        VAN_DIJK_CLIENT_ID       = Get-PersonaClientId van-dijk-transport
    })

# 1. A draft from the real model.
$accepted = $result.accepted
$draftId = $accepted.correlationId
if ($accepted.status -ne 200) { Write-Fail "Brouwer Dranken's draft got $($accepted.status), not 200 (failure code '$($accepted.draft.failureCode)'): is the Intake agent published (infra/deploy-agents.ps1) and the Agents on?" }
if ($accepted.draft.keys -join ',' -ne 'booking,missing,quotes') { Write-Fail "the answer is not a Booking draft: it has the keys $($accepted.draft.keys -join ',')" }
if ($accepted.draft.bookingRef -ne $reference) { Write-Fail 'the draft does not carry the Shipper reference the email gives' }
if ($accepted.draft.bookingRefQuote -ne $reference) { Write-Fail 'the draft gives the Shipper reference without the quote it came from' }
if (-not $draftId -or $draftId -eq 'sent-by-the-caller') { Write-Fail "API Management passed on the caller's correlation ID" }
Write-Pass "the real model drafted the booking for Brouwer Dranken: the reference with its quote (correlation ID $draftId)"

# 2. Refused for the role.
$refused = $result.refused
if ($refused.status -ne 403) { Write-Fail "Van Dijk Transport's draft got $($refused.status), not 403" }
if (-not $refused.correlationId) { Write-Fail "Van Dijk Transport's refusal carries no correlation ID" }
Write-Pass "Van Dijk Transport, without TransportOrders.Draft, is refused with 403 (correlation ID $($refused.correlationId))"

# 3. The size cap.
if ($result.toolarge.status -ne 413) { Write-Fail "a text over the size cap got $($result.toolarge.status), not 413" }
Write-Pass "a text over 40 KB is refused with 413 at the edge (correlation ID $($result.toolarge.correlationId))"

# 4. Only API Management can call the host.
if ($result.directWithoutToken -ne 401) { Write-Fail "a call straight to the host without a token got $($result.directWithoutToken), not 401" }
if ($result.directWithPersonaToken -ne 401) { Write-Fail "a call straight to the host with a Persona's token got $($result.directWithPersonaToken), not 401" }
Write-Pass 'a call straight to the host is refused with 401, without a token and with a Persona''s'

# 5. The hops: API Management, the host and the Agent run, under the one correlation ID. Ingestion takes minutes.
$recent = "TimeGenerated > ago(1h) and tostring(Properties) contains '{0}'"
$apim = $env:AZURE_ESTATE_API_MANAGEMENT
Assert-InApplicationInsights -WorkspaceId $workspace -What 'the draft, by correlation ID,' -Value $draftId -Roles $apim, 'booking-draft-api' -Filter $recent
Assert-InApplicationInsights -WorkspaceId $workspace -What "Van Dijk Transport's refused call, by correlation ID," -Value $refused.correlationId -Roles $apim -Filter $recent
$runs = Wait-Until {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "AppTraces | where TimeGenerated > ago(1h) | where tostring(Properties.correlationId) == '$draftId' and tostring(Properties.flow) == 'booking-draft' and tostring(Properties.agentId) == '$agent' and isnotempty(tostring(Properties.runId)) | summarize count()"
    $rows.Count -gt 0 -and [int] $rows[0][0] -gt 0 ? [int] $rows[0][0] : $null
}
if (-not $runs) { Write-Fail "no hop of the Agent run with the identity $agent and a run ID on the trail $draftId within 10 minutes" }
Write-Pass "the Message trail has $runs hop(s) of the Agent run, tagged with the Agent's identity $agent and a run ID"

# 6. No text of the email anywhere the Message trails read. The hops above have arrived, so the rest of that run's telemetry has too.
foreach ($sentinel in $reference, $consignee) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace "search in (AppTraces, AppRequests, AppDependencies, AppEvents, AppExceptions) '$sentinel' | where TimeGenerated > ago(1h) | count"
    $count = $rows.Count -gt 0 ? [int] $rows[0][0] : 0
    if ($count -ne 0) { Write-Fail "the email's text is in Application Insights: $count row(s) hold a sentinel of this run" }
}
Write-Pass 'no text of the email is in AppTraces, AppRequests, AppDependencies, AppEvents or AppExceptions (two sentinels searched)'

# 7. The identities and their roles.
$published = Invoke-RestMethod -Uri "$intakeEndpoint/agents/${agent}?api-version=v1" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck
$agentPrincipal = $published.instance_identity.principal_id ?? $published.identity.principal_id
if (-not $agentPrincipal) { Write-Fail "the Agent $agent is not published in the Intake project, or has no identity of its own: run infra/deploy-agents.ps1" }
if (@((Get-Graph "/servicePrincipals/$agentPrincipal/appRoleAssignments").value).Count -ne 0) { Write-Fail "the Agent's identity holds an app role" }
$account = (Invoke-Az cognitiveservices account list --resource-group $resourceGroup --query "[?kind=='AIServices'].name | [0]" --output tsv | Out-String).Trim()
$agentAzureRoles = @((Invoke-Az role assignment list --assignee $agentPrincipal --all --query '[].roleDefinitionName' --output json | Out-String | ConvertFrom-Json))
if ($agentAzureRoles.Count -ne 0) { Write-Fail "the Agent's identity holds an Azure role: $($agentAzureRoles -join ',')" }
Write-Pass "the Agent's identity holds no role: it has no tool, and calls the model only"

$projectIdentity = (Invoke-Az resource show --resource-group $resourceGroup --resource-type Microsoft.CognitiveServices/accounts/projects `
        --name "$account/norvia-intake" --query identity.principalId --output tsv | Out-String).Trim()
if ($projectIdentity) {
    if (@((Get-Graph "/servicePrincipals/$projectIdentity/appRoleAssignments").value).Count -ne 0) { Write-Fail "the Intake project's shared identity holds an app role" }
    Write-Pass "the Intake project's shared identity holds no role"
}

$hostIdentity = (Invoke-Az identity show --resource-group $resourceGroup --name $env:AZURE_ESTATE_BOOKING_DRAFT_IDENTITY_NAME --query principalId --output tsv | Out-String).Trim()
$held = @((Invoke-Az role assignment list --assignee $hostIdentity --all --query '[].{definition: roleDefinitionId, scope: scope}' --output json | Out-String | ConvertFrom-Json))
if (-not ($held.Count -eq 1 -and $held[0].definition -like "*$foundryUserRoleId" -and $held[0].scope -like '*/projects/norvia-intake')) {
    Write-Fail "the host's identity does not hold exactly Foundry User on the Intake project: $($held | ConvertTo-Json -Compress)"
}
Write-Pass "the host's identity holds Foundry User on the Intake project and nothing else"

# 8. Nothing kept. The host asks with store: false and deletes before it returns; the sweeper is the safety net, so a leftover is a NOTE and not a failure.
$list = Invoke-WebRequest -Uri "$intakeEndpoint/openai/v1/responses?limit=20&order=desc" -Headers (Get-FoundryHeader) -TimeoutSec 60 -SkipHttpErrorCheck
if ($list.StatusCode -eq 200) {
    $kept = @(($list.Content | ConvertFrom-Json).data | Where-Object { $_.created_at -ge [DateTimeOffset]::new($started).ToUnixTimeSeconds() })
    if ($kept.Count -eq 0) {
        Write-Pass 'the Intake project keeps no response of this run'
    }
    else {
        Write-Host "NOTE  the Intake project kept $($kept.Count) response(s) of this run although the host asks with store: false and deletes. The sweeper removes them; record this in docs/operations/foundry.md (spike 3)."
    }
}
else {
    Write-Host "NOTE  the Intake project's responses cannot be listed (HTTP $($list.StatusCode)); that nothing is kept is not checked. Check it in the portal."
}

Write-Host ''
Write-Host "Open the trail in the Integration console: $(Get-AppUrl integration-console-web -ResourceGroup $resourceGroup)/?key=$draftId"
