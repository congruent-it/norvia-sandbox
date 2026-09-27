#!/usr/bin/env bash
# Post-deploy presenter smoke run of Rating (P2.7, not in CI). Book two Transport Orders as Brouwer Dranken in the
# Partner portal first, from its Rotterdam warehouse to Drankenhandel De Schelde in Antwerp and to Getränke Schmitz in
# Düsseldorf, then run it, signed in to the Azure CLI as the presenter the estate was provisioned for, with the two
# Transport Order numbers the portal showed:
#
#   infra/smoke-test-rating.sh <Antwerp's Transport Order number> <Düsseldorf's Transport Order number>
#
# It checks that:
#   1. the Antwerp order is Rated, with a Freight Charge in EUR, and still waiting for planning;
#   2. the Düsseldorf order is Rating failed with NoMatchingLane, is in the rating exceptions, and is still waiting for
#      planning;
#   3. each order's intake and rating are on one Message trail, under the intake's correlation ID, with the failure code
#      for Düsseldorf's.
# The TMS's queries answer only a principal holding one of its roles, so a throwaway Container Apps job reads them as
# the intake identity. It prints only what the checks need, which this script reads from Log Analytics: never a body,
# as the Transport Orders hold the Consignee's personal data. It ends with the TMS UI's and the Integration console's
# pages to open. The presenter's steps are in docs/processes/tms/p2-rating.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
rated_number=${1:-}
failed_number=${2:-}
brouwer_dranken_id=SHP-0001

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

# Both go into the calls and queries below, so they must be what they claim to be.
for number in "$rated_number" "$failed_number"; do
  [[ $number =~ ^TO-[0-9]+$ ]] \
    || fail "usage: $0 <Antwerp's Transport Order number> <Düsseldorf's Transport Order number>, such as TO-0000042"
done
[ "$rated_number" != "$failed_number" ] || fail "the two bookings have the same Transport Order number, $rated_number"

tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
echo "TMS: $tms"
echo "Antwerp: $rated_number, Düsseldorf: $failed_number"

job="smoke-rating-$(date +%s)"
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  rm -f "$job_definition"
  az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
}
trap cleanup EXIT

# 1 and 2. The TMS's answers, as the intake identity. Rating runs just after acceptance, so an order that is still Not
# yet rated is read again for up to 2 minutes. The job is described as a definition file: `az containerapp job create
# --args` takes a leading -c for its own flag.
calls='set -eu
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
    # Only what the checks need: the Transport Order holds the Consignee'"'"'s personal data.
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
PYTHON'
jq -n \
  --arg location "$AZURE_LOCATION" \
  --arg identity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
  --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
  --arg calls "$calls" \
  --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
  --arg rated "$rated_number" --arg failed "$failed_number" '{
    location: $location,
    identity: { type: "UserAssigned", userAssignedIdentities: { ($identity): {} } },
    properties: {
      environmentId: $environment,
      configuration: {
        triggerType: "Manual",
        replicaTimeout: 600,
        replicaRetryLimit: 0,
        manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      },
      template: {
        containers: [{
          name: "smoke-rating",
          image: "mcr.microsoft.com/azure-cli:latest",
          command: ["/bin/sh", "-c", $calls],
          env: [
            { name: "TMS", value: $tms },
            { name: "TMS_API_CLIENT_ID", value: $tmsApiClientId },
            { name: "INTAKE_CLIENT_ID", value: $intakeClientId },
            { name: "RATED_NUMBER", value: $rated },
            { name: "FAILED_NUMBER", value: $failed }
          ],
          resources: { cpu: 0.5, memory: "1Gi" }
        }]
      }
    }
  }' > "$job_definition"
az containerapp job create --name "$job" --resource-group "$resource_group" --yaml "$job_definition" --output none
execution=$(az containerapp job start --name "$job" --resource-group "$resource_group" --query name --output tsv)
while
  status=$(az containerapp job execution show --name "$job" --resource-group "$resource_group" \
    --job-execution-name "$execution" --query properties.status --output tsv)
  [ "$status" = Running ] || [ "$status" = Processing ]
do
  sleep 10
done
[ "$status" = Succeeded ] || fail "reading the TMS ended $status; see the logs of job execution $execution"

workspace=$(az monitor log-analytics workspace show --resource-group "$resource_group" \
  --workspace-name "$AZURE_ESTATE_LOG_ANALYTICS_WORKSPACE" --query customerId --output tsv)
logs() {
  az rest --method post --resource https://api.loganalytics.io --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
    --body "$(jq -n --arg query "$1" '{query: $query, timespan: "P1D"}')" --query 'tables[0].rows' --output json
}
# Ingestion takes a few minutes, so a check that finds nothing asks again, for up to 10 minutes.
until_found() {
  local result
  for _ in $(seq 1 30); do
    result=$("$@")
    [ -z "$result" ] || { echo "$result"; return; }
    sleep 20
  done
}
job_result() {
  logs "ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$job' and Log_s startswith 'SMOKE-RESULT ' | project Log_s" \
    | jq -r '.[0][0] // empty' | sed 's/^SMOKE-RESULT //'
}
result=$(until_found job_result)
[ -n "$result" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"

for list in ratingExceptions waitingForPlanning; do
  jq -e --arg list "$list" '.[$list] | type == "array"' <<<"$result" >/dev/null \
    || fail "the TMS answered $(jq -r --arg list "$list" '.[$list].httpStatus' <<<"$result") for the $list list"
done

# The TMS's answer for one order, failing when it isn't there or isn't Brouwer Dranken's booking to the country asked.
order_read() {
  local key=$1 number=$2 to=$3
  jq -e --arg key "$key" '.[$key].httpStatus | not' <<<"$result" >/dev/null \
    || fail "the TMS answered $(jq -r --arg key "$key" '.[$key].httpStatus' <<<"$result") for Transport Order $number"
  jq -e --arg key "$key" --arg shipper "$brouwer_dranken_id" --arg to "$to" \
    '.[$key] | .shipperId == $shipper and .from == "NL" and .to == $to' <<<"$result" >/dev/null \
    || fail "Transport Order $number isn't Brouwer Dranken's Transport Order from NL to $to: $(jq -c --arg key "$key" '.[$key] | {shipperId, from, to}' <<<"$result")"
  # It goes into the queries below, so it must be what it claims to be.
  [[ $(jq -r --arg key "$key" '.[$key].correlationId' <<<"$result") =~ ^[0-9a-fA-F-]{36}$ ]] \
    || fail "Transport Order $number's correlation ID isn't one: $(jq -r --arg key "$key" '.[$key].correlationId' <<<"$result")"
}

# 1. Antwerp: Rated, with a Freight Charge, and still waiting for planning.
order_read rated "$rated_number" BE
jq -e '.rated.ratingResult == "Rated"' <<<"$result" >/dev/null \
  || fail "Transport Order $rated_number is $(jq -r '.rated.ratingResult' <<<"$result"), not Rated$(jq -r '.rated.ratingFailure // empty | " (\(.))"' <<<"$result")"
jq -e '.rated.freightCharge | .currency == "EUR" and .total > 0 and (.lines | any(. == "Freight"))' <<<"$result" >/dev/null \
  || fail "Transport Order $rated_number has no Freight Charge with a Freight line: $(jq -c '.rated.freightCharge' <<<"$result")"
pass "Transport Order $rated_number to Antwerp is Rated: $(jq -r '.rated.freightCharge | "\(.currency) \(.total), \(.lines | join(" + "))"' <<<"$result")"
jq -e --arg number "$rated_number" '.rated.status == "Accepted" and (.waitingForPlanning | any(.number == $number))' <<<"$result" >/dev/null \
  || fail "Transport Order $rated_number isn't waiting for planning"
jq -e --arg number "$rated_number" '.ratingExceptions | all(.number != $number)' <<<"$result" >/dev/null \
  || fail "Transport Order $rated_number is Rated, but in the rating exceptions"
pass "Transport Order $rated_number is waiting for planning, and not in the rating exceptions"

# 2. Düsseldorf: Rating failed with NoMatchingLane, in the rating exceptions, and still waiting for planning.
order_read failed "$failed_number" DE
jq -e '.failed | .ratingResult == "RatingFailed" and .ratingFailure == "NoMatchingLane" and .freightCharge == null' <<<"$result" >/dev/null \
  || fail "Transport Order $failed_number isn't Rating failed with NoMatchingLane: $(jq -c '.failed | {ratingResult, ratingFailure}' <<<"$result")"
pass "Transport Order $failed_number to Düsseldorf is Rating failed with NoMatchingLane, and has no Freight Charge"
jq -e --arg number "$failed_number" '.ratingExceptions | any(.number == $number and .code == "NoMatchingLane")' <<<"$result" >/dev/null \
  || fail "Transport Order $failed_number isn't in the rating exceptions with NoMatchingLane"
pass "Transport Order $failed_number is in the rating exceptions with NoMatchingLane"
jq -e --arg number "$failed_number" '.failed.status == "Accepted" and (.waitingForPlanning | any(.number == $number and .ratingResult == "RatingFailed"))' <<<"$result" >/dev/null \
  || fail "Transport Order $failed_number isn't waiting for planning"
pass "Transport Order $failed_number is still waiting for planning"

# 3. Each rating on its order's Message trail: the tracking contract's keys (coding guidelines §7), which the TMS logs
# straight in Properties, under the intake's correlation ID. This reads the keys as recorded, not as the Integration
# console shows them. Azure Monitor names the TMS after its Container Apps environment too: [<environment>]/tms-api.
trail_keys() {
  logs "union AppTraces, AppDependencies
| where Properties has '$1' and tostring(Properties.correlationId) =~ '$1' and AppRoleName endswith 'tms-api'
| extend flow = tostring(Properties.flow)
| summarize flows = make_set_if(flow, isnotempty(flow)),
    numbers = make_set_if(tostring(Properties.transportOrderNumber), flow == 'transport-order-rating'),
    parties = make_set_if(tostring(Properties.partyId), flow == 'transport-order-rating'),
    failureCodes = make_set_if(tostring(Properties.failureCode), flow == 'transport-order-rating' and isnotempty(tostring(Properties.failureCode)))" \
    | jq -c '.[0] // empty | {flows: (.[0] | fromjson | sort), numbers: (.[1] | fromjson), parties: (.[2] | fromjson), failureCodes: (.[3] | fromjson)}'
}
# Found once the trail holds the intake and the rating, and the failure codes expected: ingestion may bring the rating's
# rows in more than one batch.
rating_on_trail() {
  trail_keys "$1" | jq -c --argjson codes "$2" \
    'select(.flows == ["transport-order-intake", "transport-order-rating"] and .failureCodes == $codes)'
}
check_trail() {
  local key=$1 number=$2 failure_codes=$3 correlation_id trail
  correlation_id=$(jq -r --arg key "$key" '.[$key].correlationId' <<<"$result")
  trail=$(until_found rating_on_trail "$correlation_id" "$failure_codes")
  [ -n "$trail" ] || fail "the Message trail $correlation_id doesn't hold Transport Order $number's intake and rating with failure codes $failure_codes after 10 minutes: $(trail_keys "$correlation_id")"
  jq -e --arg number "$number" --arg party "$brouwer_dranken_id" '.numbers == [$number] and .parties == [$party]' <<<"$trail" >/dev/null \
    || fail "the rating on the Message trail $correlation_id isn't Transport Order $number's for $brouwer_dranken_id: $trail"
  pass "Transport Order $number's intake and rating are on its Message trail $correlation_id$(jq -r '.failureCodes | if length > 0 then ", with failure code \(join(", "))" else "" end' <<<"$trail")"
}
check_trail rated "$rated_number" '[]'
check_trail failed "$failed_number" '["NoMatchingLane"]'

tms_ui="https://$(az containerapp show --name tms-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Open in the TMS UI:"
echo "  Rated:              $tms_ui/transport-orders/$rated_number"
echo "  rating exceptions:  $tms_ui/rating-exceptions"
echo "  Rating failed:      $tms_ui/transport-orders/$failed_number"
echo "Open in the Integration console:"
echo "  Antwerp's trail:    $console/trails/$(jq -r '.rated.correlationId' <<<"$result")"
echo "  Düsseldorf's trail: $console/trails/$(jq -r '.failed.correlationId' <<<"$result")"
