#Requires -Version 7.4
<#
.SYNOPSIS
    Post-deploy presenter smoke run of Rating (P2.7, not in CI).
.DESCRIPTION
    Book two Transport Orders as Brouwer Dranken in the Partner portal first, from its Rotterdam warehouse to Drankenhandel De
    Schelde in Antwerp and to Getränke Schmitz in Düsseldorf, then run it, signed in to the Azure CLI as the presenter the estate
    was provisioned for, with the two Transport Order numbers the portal showed:

        infra/smoke-test-rating.ps1 <Antwerp's Transport Order number> <Düsseldorf's Transport Order number>

    It checks that:
      1. the Antwerp order is Rated, with a Freight Charge in EUR, and still waiting for planning;
      2. the Düsseldorf order is Rating failed with NoMatchingLane, is in the rating exceptions, and is still waiting for
         planning;
      3. each order's intake and rating are on one Message trail, under the intake's correlation ID, the rating with the
         outcome Succeeded, and with the failure code for Düsseldorf's.
    The TMS's queries answer only a principal holding one of its roles, so a throwaway Container Apps job reads them as
    the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a body,
    as the Transport Orders hold the Consignee's personal data. It ends with the TMS UI's and the Integration console's
    pages to open. The presenter's steps are in docs/processes/tms/p2-rating.md.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string] $RatedNumber = '',
    [Parameter(Position = 1)][string] $FailedNumber = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib/Norvia.Infra.psm1') -Force

Import-AzdEnvironment
$resourceGroup = Get-ResourceGroupName
$brouwerDrankenId = 'SHP-0001'

# Both go into the calls and queries below, so they must be what they claim to be.
foreach ($number in $RatedNumber, $FailedNumber) {
    if ($number -notmatch '^TO-[0-9]+$') {
        Write-Fail "usage: smoke-test-rating.ps1 <Antwerp's Transport Order number> <Düsseldorf's Transport Order number>, such as TO-0000042"
    }
}
if ($RatedNumber -eq $FailedNumber) { Write-Fail "the two bookings have the same Transport Order number, $RatedNumber" }

$tms = Get-AppUrl tms-api -ResourceGroup $resourceGroup
Write-Host "TMS: $tms"
Write-Host "Antwerp: $RatedNumber, Düsseldorf: $FailedNumber"

# 1 and 2. The TMS's answers, as the intake identity. Rating runs just after acceptance, so an order that is still Not
# yet rated is read again for up to 2 minutes.
$calls = @'
set -eu
az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
TOKEN=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
export TOKEN
python3 - <<"PYTHON"
import json, os, time, urllib.error, urllib.request

numbers = {os.environ["RATED_NUMBER"], os.environ["FAILED_NUMBER"]}

def get(path):
    request = urllib.request.Request(os.environ["TMS"] + path, headers={"Authorization": "Bearer " + os.environ["TOKEN"]})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"httpStatus": error.code}

def order(number):
    for _ in range(12):
        read = get("/v1/transport-orders/" + number)
        if read.get("ratingResult") != "NotYetRated":
            break
        time.sleep(10)
    if "httpStatus" in read:
        return read
    charge = read.get("freightCharge")
    failure = read.get("ratingFailure")
    details = read.get("transportOrder") or {}
    country = lambda leg: (((details.get(leg) or {}).get("address")) or {}).get("country")
    # Only what the checks need: the Transport Order holds the Consignee's personal data.
    return {
        "shipperId": read["shipper"]["id"],
        "status": read["status"],
        "correlationId": read["correlationId"],
        "from": country("collection"),
        "to": country("delivery"),
        "ratingResult": read["ratingResult"],
        "freightCharge": None if charge is None else {
            "currency": charge["currency"], "total": charge["total"],
            "lines": [line["type"] for line in charge["chargeLines"]]},
        "ratingFailure": None if failure is None else failure["code"],
    }

exceptions = get("/v1/transport-orders/rating-exceptions")
waiting = get("/v1/transport-orders?status=Accepted")
print("SMOKE-RESULT " + json.dumps({
    "rated": order(os.environ["RATED_NUMBER"]),
    "failed": order(os.environ["FAILED_NUMBER"]),
    "ratingExceptions": exceptions if isinstance(exceptions, dict) else [
        {"number": e["transportOrderNumber"], "code": e["ratingFailure"]["code"]}
        for e in exceptions if e["transportOrderNumber"] in numbers],
    "waitingForPlanning": waiting if isinstance(waiting, dict) else [
        {"number": w["transportOrderNumber"], "ratingResult": w["ratingResult"]}
        for w in waiting if w["transportOrderNumber"] in numbers],
}))
PYTHON
'@
$result = Invoke-ThrowawayJobForResult -Prefix smoke-rating -Calls $calls -IdentityId $env:AZURE_ESTATE_INTAKE_IDENTITY_ID `
    -ResourceGroup $resourceGroup -Purpose 'reading the TMS' -ReplicaTimeout 600 -Environment ([ordered]@{
        TMS               = $tms
        TMS_API_CLIENT_ID = $env:AZURE_ESTATE_TMS_API_CLIENT_ID
        INTAKE_CLIENT_ID  = $env:AZURE_ESTATE_INTAKE_CLIENT_ID
        RATED_NUMBER      = $RatedNumber
        FAILED_NUMBER     = $FailedNumber
    })
$workspace = Get-LogAnalyticsWorkspaceId -WorkspaceName $env:AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE -ResourceGroup $resourceGroup

foreach ($list in 'ratingExceptions', 'waitingForPlanning') {
    if ($result.$list -isnot [array]) { Write-Fail "the TMS answered $($result.$list.httpStatus) for the $list list" }
}

# The TMS's answer for one order, failing when it isn't there or isn't Brouwer Dranken's booking to the country asked.
function Assert-OrderRead($Order, [string] $Number, [string] $To) {
    if ($Order.httpStatus) { Write-Fail "the TMS answered $($Order.httpStatus) for Transport Order $Number" }
    if (-not ($Order.shipperId -eq $brouwerDrankenId -and $Order.from -eq 'NL' -and $Order.to -eq $To)) {
        Write-Fail "Transport Order $Number isn't Brouwer Dranken's Transport Order from NL to ${To}: $(@{ shipperId = $Order.shipperId; from = $Order.from; to = $Order.to } | ConvertTo-Json -Compress)"
    }
    # It goes into the queries below, so it must be what it claims to be.
    if ($Order.correlationId -notmatch '^[0-9a-fA-F-]{36}$') { Write-Fail "Transport Order $Number's correlation ID isn't one: $($Order.correlationId)" }
}

# 1. Antwerp: Rated, with a Freight Charge, and still waiting for planning.
$rated = $result.rated
Assert-OrderRead $rated $RatedNumber BE
if ($rated.ratingResult -ne 'Rated') {
    Write-Fail "Transport Order $RatedNumber is $($rated.ratingResult), not Rated$($rated.ratingFailure ? " ($($rated.ratingFailure))" : '')"
}
$charge = $rated.freightCharge
if (-not ($charge -and $charge.currency -eq 'EUR' -and $charge.total -gt 0 -and $charge.lines -contains 'Freight')) {
    Write-Fail "Transport Order $RatedNumber has no Freight Charge with a Freight line: $($charge | ConvertTo-Json -Compress)"
}
Write-Pass "Transport Order $RatedNumber to Antwerp is Rated: $($charge.currency) $($charge.total), $($charge.lines -join ' + ')"
if (-not ($rated.status -eq 'Accepted' -and @($result.waitingForPlanning | Where-Object number -EQ $RatedNumber).Count -gt 0)) {
    Write-Fail "Transport Order $RatedNumber isn't waiting for planning"
}
if (@($result.ratingExceptions | Where-Object number -EQ $RatedNumber).Count -gt 0) {
    Write-Fail "Transport Order $RatedNumber is Rated, but in the rating exceptions"
}
Write-Pass "Transport Order $RatedNumber is waiting for planning, and not in the rating exceptions"

# 2. Düsseldorf: Rating failed with NoMatchingLane, in the rating exceptions, and still waiting for planning.
$failed = $result.failed
Assert-OrderRead $failed $FailedNumber DE
if (-not ($failed.ratingResult -eq 'RatingFailed' -and $failed.ratingFailure -eq 'NoMatchingLane' -and $null -eq $failed.freightCharge)) {
    Write-Fail "Transport Order $FailedNumber isn't Rating failed with NoMatchingLane: $(@{ ratingResult = $failed.ratingResult; ratingFailure = $failed.ratingFailure } | ConvertTo-Json -Compress)"
}
Write-Pass "Transport Order $FailedNumber to Düsseldorf is Rating failed with NoMatchingLane, and has no Freight Charge"
if (@($result.ratingExceptions | Where-Object { $_.number -eq $FailedNumber -and $_.code -eq 'NoMatchingLane' }).Count -eq 0) {
    Write-Fail "Transport Order $FailedNumber isn't in the rating exceptions with NoMatchingLane"
}
Write-Pass "Transport Order $FailedNumber is in the rating exceptions with NoMatchingLane"
if (-not ($failed.status -eq 'Accepted' -and @($result.waitingForPlanning | Where-Object { $_.number -eq $FailedNumber -and $_.ratingResult -eq 'RatingFailed' }).Count -gt 0)) {
    Write-Fail "Transport Order $FailedNumber isn't waiting for planning"
}
Write-Pass "Transport Order $FailedNumber is still waiting for planning"

# 3. Each rating on its order's Message trail: the tracking contract's keys (coding guidelines §7), which the TMS logs
# straight in Properties, under the intake's correlation ID. This reads the keys as recorded, not as the Integration
# console shows them. Azure Monitor names the TMS after its Container Apps environment too: [<environment>]/tms-api.
function Get-TrailKeys([string] $CorrelationId) {
    $rows = Invoke-LogAnalyticsQuery -WorkspaceId $workspace @"
union AppTraces, AppDependencies
| where Properties has '$CorrelationId' and tostring(Properties.correlationId) =~ '$CorrelationId' and AppRoleName endswith 'tms-api'
| extend flow = tostring(Properties.flow)
| summarize flows = make_set_if(flow, isnotempty(flow)),
    numbers = make_set_if(tostring(Properties.transportOrderNumber), flow == 'transport-order-rating'),
    parties = make_set_if(tostring(Properties.partyId), flow == 'transport-order-rating'),
    outcomes = make_set_if(tostring(Properties.outcome), flow == 'transport-order-rating' and isnotempty(tostring(Properties.outcome))),
    failureCodes = make_set_if(tostring(Properties.failureCode), flow == 'transport-order-rating' and isnotempty(tostring(Properties.failureCode)))
"@
    if ($rows.Count -eq 0) { return $null }
    $row = $rows[0]
    [pscustomobject]@{
        flows        = @(($row[0] | ConvertFrom-Json) | Sort-Object)
        numbers      = @($row[1] | ConvertFrom-Json)
        parties      = @($row[2] | ConvertFrom-Json)
        outcomes     = @($row[3] | ConvertFrom-Json)
        failureCodes = @($row[4] | ConvertFrom-Json)
    }
}

function Assert-Trail([string] $Key, [string] $Number, [string[]] $FailureCodes) {
    $correlationId = $result.$Key.correlationId
    # Found once the trail holds the intake and the rating, the rating's outcome and the failure codes expected: ingestion
    # may bring the rating's rows in more than one batch.
    $trail = Wait-Until {
        $keys = Get-TrailKeys $correlationId
        ($keys -and (Test-SameValues $keys.flows 'transport-order-intake', 'transport-order-rating') -and (Test-SameValues $keys.outcomes 'Succeeded') `
            -and (Test-SameValues $keys.failureCodes $FailureCodes)) ? $keys : $null
    }
    if (-not $trail) {
        Write-Fail "the Message trail $correlationId doesn't hold Transport Order $Number's intake and its rating that Succeeded with failure codes $($FailureCodes -join ',') after 10 minutes: $(Get-TrailKeys $correlationId | ConvertTo-Json -Compress)"
    }
    if (-not ((Test-SameValues $trail.numbers $Number) -and (Test-SameValues $trail.parties $brouwerDrankenId))) {
        Write-Fail "the rating on the Message trail $correlationId isn't Transport Order $Number's for ${brouwerDrankenId}: $($trail | ConvertTo-Json -Compress)"
    }
    $codes = $trail.failureCodes.Count -gt 0 ? ", with failure code $($trail.failureCodes -join ', ')" : ''
    Write-Pass "Transport Order $Number's intake and rating are on its Message trail $correlationId, the rating Succeeded$codes"
}
Assert-Trail rated $RatedNumber @()
Assert-Trail failed $FailedNumber @('NoMatchingLane')

$tmsUi = Get-AppUrl tms-web -ResourceGroup $resourceGroup
$console = Get-AppUrl integration-console-web -ResourceGroup $resourceGroup
Write-Host ''
Write-Host 'Open in the TMS UI:'
Write-Host "  Rated:              $tmsUi/transport-orders/$RatedNumber"
Write-Host "  rating exceptions:  $tmsUi/rating-exceptions"
Write-Host "  Rating failed:      $tmsUi/transport-orders/$FailedNumber"
Write-Host 'Open in the Integration console:'
Write-Host "  Antwerp's trail:    $console/trails/$($result.rated.correlationId)"
Write-Host "  Düsseldorf's trail: $console/trails/$($result.failed.correlationId)"
