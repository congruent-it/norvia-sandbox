#!/usr/bin/env bash
# Post-deploy presenter smoke run of Shipper visibility (P8.8, not in CI). It re-enacts the headline incident from the Shipper's
# side: user story 14 (Brouwer Dranken watches its tracking screen while Kessler Spedition in Auto-progress delivers a Transport
# Order) and user story 15 (the same screen stays on "In transit" when a Carrier's status code is outside P5's mapping table).
# The Shipper's screen and the Planner's release are the presenter's, in the Partner portal and the TMS UI; the script stops and
# says what to do. Run it in a terminal, signed in to the Azure CLI as the presenter the estate was provisioned for, with two
# Transport Order numbers of Brouwer Dranken:
#
#   infra/smoke-test-shipper-visibility.sh <story 14 Transport Order number> <story 15 Transport Order number>
#
# Get the first Planned and assigned to Kessler Spedition but NOT released, so it is Scheduled and the presenter can release it
# with the screen open (leave Kessler Spedition on Auto-confirm and Auto-progress). Get the second Booked and carried by Van Dijk
# Transport, as in the presenter smoke run of Carrier booking (docs/processes/tms/p4-carrier-booking.md), with Van Dijk
# Transport's tracking on Manual. Do not track either first.
# It checks that:
#   1. story 14: through API Management, as Brouwer Dranken, the Transport Order is SCHEDULED; once released it only moves
#      forward and ends DELIVERED with a Proof of delivery, whose sample CMR downloads; the presenter saw the screen do it;
#   2. a Carrier Persona's token (Van Dijk Transport's) against the tracking API gets 403 Forbidden;
#   3. story 15: PU and DEP as Van Dijk Transport return 202 Accepted and the Shipper sees IN_TRANSIT; then a status code outside
#      the mapping table (DLV) also returns 202 Accepted, yet the Shipper still sees IN_TRANSIT with the same last update
#      a minute and a half later;
#   4. with the TMS stopped for API Management, a query gets 503 with a correlation ID, the presenter sees the error with the
#      time of the last answer on the screen, and the query works again once it is back;
#   5. the queries are findable in Application Insights by correlation ID, in API Management and, where the TMS answered, the TMS.
# A Persona's token can be had only as the Partner portal's identity (ADR 0005) and the TMS answers only a principal holding one
# of its roles, so a throwaway Container Apps job makes the calls as the Partner portal and reads the TMS as the intake
# identity. P5's presenter-panel fault isn't built yet, so check 3 plays what it does: the Carrier sends a code the Carrier-
# tracking Logic App can't map. The script prints only what the checks need: never a body, a token or a key.
# Check 4 stops the TMS by pointing API Management's tms-api backend at an address that answers nothing, and restores it
# afterwards, even when the run fails. The presenter's steps are in docs/processes/tms/p8-shipper-visibility.md.
set -euo pipefail

eval "$(azd env get-values)"
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
story_14=${1:-}
story_15=${2:-}
van_dijk_id=CAR-0001

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

[[ $story_14 =~ ^TO-[0-9]+$ && $story_15 =~ ^TO-[0-9]+$ ]] \
  || fail "usage: $0 <story 14 Transport Order number> <story 15 Transport Order number>, such as TO-0000042 TO-0000043"
[ -t 0 ] || fail "run this in a terminal: the presenter's steps wait for Enter"

arm="https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers"
partner_api=$AZURE_ESTATE_PARTNER_API_URL
tms="https://$(az containerapp show --name tms-api --resource-group "$resource_group" --query properties.configuration.ingress.fqdn --output tsv)"
tenant=$(az account show --query tenantId --output tsv)
persona_client_id() { tr ';' '\n' <<<"$AZURE_ESTATE_PERSONA_CLIENT_IDS" | sed -n "s/^$1=//p"; }
subscription_key() {
  az rest --method post --url "$arm/Microsoft.ApiManagement/service/$AZURE_ESTATE_API_MANAGEMENT/subscriptions/$1/listSecrets?api-version=2024-05-01" \
    --query primaryKey --output tsv
}
brouwer_client_id=$(persona_client_id brouwer-dranken)
van_dijk_client_id=$(persona_client_id van-dijk-transport)
brouwer_key=$(subscription_key brouwer-dranken)
van_dijk_key=$(subscription_key van-dijk-transport)

backend="$arm/Microsoft.ApiManagement/service/$AZURE_ESTATE_API_MANAGEMENT/backends/tms-api?api-version=2024-05-01"
backend_url=$(az rest --method get --url "$backend" --query properties.url --output tsv)
set_backend() {
  az rest --method patch --url "$backend" --headers "If-Match=*" --body "$(jq -n --arg url "$1" '{properties: {url: $url}}')" --output none
}
# Put back whatever API Management was calling, if the run ends while it is pointed away from the TMS.
restore_tms=
job_file=$(mktemp)
job_definition=$(mktemp --suffix=.yaml)
cleanup() {
  local job
  job=$(cat "$job_file")
  rm -f "$job_definition" "$job_file"
  [ -z "$job" ] || az containerapp job delete --name "$job" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  if [ -n "$restore_tms" ]; then
    set_backend "$backend_url" || echo "Put API Management's tms-api backend back to $backend_url" >&2
  fi
}
trap cleanup EXIT

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
confirm() {
  local answer
  read -r -p "$1 [y/n] " answer
  [[ $answer =~ ^[Yy] ]]
}

# Runs a list of steps in a throwaway Container Apps job and prints what it saw, as one JSON document. $1 is a JSON array of
# steps, run in order, each with a name:
#   {do: "get", as: "brouwer"|"vandijk", path}                    one call; httpStatus, correlationId, and for an order its status
#   {do: "watch", number, seconds, until}                         polls the Shipper's view every 3 seconds; seen statuses in order
#   {do: "download", number}                                      the first file of the current Proof of delivery
#   {do: "send", code, offset}                                    a tracking event as Van Dijk Transport for Transport Order $NUMBER
# The job is described as a definition file: `az containerapp job create --args` takes a leading -c for its own flag.
calls='set -eu
token_for() {
  az login --identity --client-id "$PARTNER_PORTAL_CLIENT_ID" --allow-no-subscriptions --output none
  assertion=$(az account get-access-token --resource api://AzureADTokenExchange --query accessToken --output tsv)
  curl -fsS "https://login.microsoftonline.com/$TENANT/oauth2/v2.0/token" \
    -d grant_type=client_credentials -d client_id="$1" -d scope="$PARTNER_API_CLIENT_ID/.default" \
    -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer -d client_assertion="$assertion" \
    | python3 -c "import json, sys; print(json.load(sys.stdin)[\"access_token\"])"
}
BROUWER_TOKEN=$(token_for "$BROUWER_CLIENT_ID")
VAN_DIJK_TOKEN=$(token_for "$VAN_DIJK_CLIENT_ID")
export BROUWER_TOKEN VAN_DIJK_TOKEN
az login --identity --client-id "$INTAKE_CLIENT_ID" --allow-no-subscriptions --output none
TMS_TOKEN=$(az account get-access-token --resource "$TMS_API_CLIENT_ID" --query accessToken --output tsv)
export TMS_TOKEN
python3 - <<"PYTHON"
import json, os, time, urllib.error, urllib.request
from datetime import datetime, timedelta, timezone

def call(method, url, headers, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            text = response.read()
            return response.status, response.headers, text
    except urllib.error.HTTPError as error:
        return error.code, error.headers, b""
    except OSError:
        return 0, {}, b""

def parse(text):
    try:
        return json.loads(text) if text else None
    except ValueError:
        return None

def edge(who, path):
    token, key = (os.environ["BROUWER_TOKEN"], os.environ["BROUWER_KEY"]) if who == "brouwer" else (os.environ["VAN_DIJK_TOKEN"], os.environ["VAN_DIJK_KEY"])
    return call("GET", os.environ["PARTNER_API"] + path, {"Authorization": "Bearer " + token, "Ocp-Apim-Subscription-Key": key})

def tms(path):
    status, _, text = call("GET", os.environ["TMS"] + path, {"Authorization": "Bearer " + os.environ["TMS_TOKEN"]})
    return parse(text) if status == 200 else {"httpStatus": status}

def view(number):
    status, headers, text = edge("brouwer", "/transport-orders/" + number)
    body = parse(text) if status == 200 else None
    return status, headers.get("x-correlation-id"), body

results = []
for index, step in enumerate(json.loads(os.environ["STEPS"])):
    seen = {"name": step["name"]}
    if step["do"] == "get":
        status, headers, text = edge(step["as"], step["path"])
        body = parse(text) if status == 200 else None
        seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"))
        if isinstance(body, dict):
            seen["status"] = body.get("status")
    elif step["do"] == "watch":
        statuses, last_updates, started = [], [], time.time()
        while time.time() - started < step["seconds"]:
            status, correlation_id, body = view(step["number"])
            if body:
                if not statuses or statuses[-1] != body["status"]:
                    statuses.append(body["status"])
                if not last_updates or last_updates[-1] != body.get("lastUpdate"):
                    last_updates.append(body.get("lastUpdate"))
                seen["correlationId"] = correlation_id
                seen["files"] = [f["fileId"] for f in (body.get("proofOfDelivery") or {}).get("files", [])]
                seen["codes"] = [e["code"] for e in body.get("events", [])]
                if step["until"] and body["status"] == step["until"]:
                    break
            time.sleep(3)
        seen.update(statuses=statuses, lastUpdates=last_updates)
    elif step["do"] == "download":
        _, _, body = view(step["number"])
        files = (body or {}).get("proofOfDelivery", {}).get("files", [])
        if files:
            status, headers, content = edge("brouwer", "/transport-orders/%s/proof-of-delivery/files/%s" % (step["number"], files[0]["fileId"]))
            seen.update(httpStatus=status, contentType=headers.get("content-type"), bytes=len(content), correlationId=headers.get("x-correlation-id"))
        else:
            seen.update(httpStatus=None)
    elif step["do"] == "send":
        order = tms("/v1/transport-orders/" + os.environ["NUMBER"])
        shipment = tms("/v1/shipments/" + order["shipmentNumber"]) if order.get("shipmentNumber") else {}
        confirmed = [b for b in shipment.get("carrierBookings", []) if b["status"] == "Confirmed"]
        seen["carrierId"] = (shipment.get("carriedBy", {}).get("carrier") or {}).get("id")
        if confirmed:
            # Event times are local to Rotterdam or Duesseldorf, so one read as UTC is always in the past.
            when = datetime.now(timezone.utc).replace(tzinfo=None, microsecond=0) - timedelta(hours=2) + timedelta(minutes=step["offset"])
            event = {"bookingRef": confirmed[0]["carrierBookingReference"], "eventId": "SMOKE-%s-%d" % (os.environ["RUN"], index), "statusCode": step["code"],
                     "eventTime": when.isoformat(), "place": "Rotterdam", "postcode": "3011 AA", "country": "NLD"}
            status, headers, _ = call("POST", os.environ["PARTNER_API"] + "/carrier-tracking", {
                "Authorization": "Bearer " + os.environ["VAN_DIJK_TOKEN"], "Ocp-Apim-Subscription-Key": os.environ["VAN_DIJK_KEY"],
                "Content-Type": "application/json"}, event)
            seen.update(httpStatus=status, correlationId=headers.get("x-correlation-id"))
    results.append(seen)
print("SMOKE-RESULT " + json.dumps(results))
PYTHON'
run_steps() {
  local job="smoke-shipper-visibility-$(date +%s)"
  echo "$job" > "$job_file"
  run=$(date +%Y%m%d%H%M%S%N)
  jq -n \
    --arg location "$AZURE_LOCATION" \
    --arg portalIdentity "$AZURE_ESTATE_PARTNER_PORTAL_IDENTITY_ID" --arg intakeIdentity "$AZURE_ESTATE_INTAKE_IDENTITY_ID" \
    --arg environment "$(az containerapp env list --resource-group "$resource_group" --query '[0].id' --output tsv)" \
    --arg calls "$calls" --arg job "$job" --arg tenant "$tenant" --arg partnerApi "$partner_api" --arg partnerApiClientId "$AZURE_ESTATE_PARTNER_API_CLIENT_ID" \
    --arg brouwer "$brouwer_client_id" --arg brouwerKey "$brouwer_key" --arg vanDijk "$van_dijk_client_id" --arg vanDijkKey "$van_dijk_key" \
    --arg portalClientId "$AZURE_ESTATE_PARTNER_PORTAL_CLIENT_ID" --arg intakeClientId "$AZURE_ESTATE_INTAKE_CLIENT_ID" \
    --arg tms "$tms" --arg tmsApiClientId "$AZURE_ESTATE_TMS_API_CLIENT_ID" --arg number "$2" \
    --arg run "$run" --arg steps "$1" '{
      location: $location,
      identity: { type: "UserAssigned", userAssignedIdentities: { ($portalIdentity): {}, ($intakeIdentity): {} } },
      properties: {
        environmentId: $environment,
        configuration: {
          triggerType: "Manual",
          replicaTimeout: 900,
          replicaRetryLimit: 0,
          manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 },
          secrets: [ { name: "brouwer-key", value: $brouwerKey }, { name: "van-dijk-key", value: $vanDijkKey } ]
        },
        template: {
          containers: [{
            name: $job,
            image: "mcr.microsoft.com/azure-cli:latest",
            command: ["/bin/sh", "-c", $calls],
            env: [
              { name: "TENANT", value: $tenant },
              { name: "PARTNER_API", value: $partnerApi },
              { name: "PARTNER_API_CLIENT_ID", value: $partnerApiClientId },
              { name: "PARTNER_PORTAL_CLIENT_ID", value: $portalClientId },
              { name: "BROUWER_CLIENT_ID", value: $brouwer },
              { name: "BROUWER_KEY", secretRef: "brouwer-key" },
              { name: "VAN_DIJK_CLIENT_ID", value: $vanDijk },
              { name: "VAN_DIJK_KEY", secretRef: "van-dijk-key" },
              { name: "INTAKE_CLIENT_ID", value: $intakeClientId },
              { name: "TMS", value: $tms },
              { name: "TMS_API_CLIENT_ID", value: $tmsApiClientId },
              { name: "NUMBER", value: $number },
              { name: "RUN", value: $run },
              { name: "STEPS", value: $steps }
            ],
            resources: { cpu: 0.5, memory: "1Gi" }
          }]
        }
      }
    }' > "$job_definition"
  az containerapp job create --name "$job" --resource-group "$resource_group" --yaml "$job_definition" --output none
  local execution status
  execution=$(az containerapp job start --name "$job" --resource-group "$resource_group" --query name --output tsv)
  while
    status=$(az containerapp job execution show --name "$job" --resource-group "$resource_group" \
      --job-execution-name "$execution" --query properties.status --output tsv)
    [ "$status" = Running ] || [ "$status" = Processing ]
  do
    sleep 10
  done
  [ "$status" = Succeeded ] || fail "the job's calls ended $status; see the logs of job execution $execution"
  local ran=$job
  job_result() {
    logs "ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$ran' and Log_s startswith 'SMOKE-RESULT ' | project Log_s" \
      | jq -r '.[0][0] // empty' | sed 's/^SMOKE-RESULT //'
  }
  until_found job_result
  az containerapp job delete --name "$ran" --resource-group "$resource_group" --yes --output none 2>/dev/null || true
  : > "$job_file"
}
step() { jq -e --arg name "$1" '.[] | select(.name == $name)' <<<"$result"; }
# Runs steps and puts what they saw in $result.
run() {
  result=$(run_steps "$@")
  [ -n "$result" ] || fail "the job's results didn't reach Log Analytics in 10 minutes"
}

portal="https://$(az containerapp show --name partner-portal-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
tms_ui="https://$(az containerapp show --name tms-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo "Partner portal: $portal"
echo "Story 14 Transport Order: $story_14"
echo "Story 15 Transport Order: $story_15"

# 1. Story 14.
run "$(jq -nc --arg n "$story_14" '[{name: "before", do: "get", as: "brouwer", path: ("/transport-orders/" + $n)}]')" "$story_14"
[ "$(step before | jq -r .httpStatus)" = 200 ] \
  || fail "Brouwer Dranken can't read Transport Order $story_14 through API Management: $(step before | jq -c '{httpStatus}')"
[ "$(step before | jq -r .status)" = SCHEDULED ] \
  || fail "Transport Order $story_14 isn't SCHEDULED (Planned, assigned to Kessler Spedition, not released): $(step before | jq -c '{status}')"
pass "Brouwer Dranken sees Transport Order $story_14 as SCHEDULED"
echo
echo "Open Brouwer Dranken's tracking screen and the Transport Order, and keep it open:"
echo "  $portal/personas/brouwer-dranken/tracking/$story_14"
echo "Then release its Shipment in the TMS UI (Kessler Spedition confirms and progresses it by itself):"
echo "  $tms_ui"
read -r -p "Press Enter once you released it... "
run "$(jq -nc --arg n "$story_14" '[
  {name: "watch", do: "watch", number: $n, seconds: 600, until: "DELIVERED"},
  {name: "download", do: "download", number: $n}]')" "$story_14"
jq -e '(.statuses | length > 0) and (.statuses | last) == "DELIVERED"' <<<"$(step watch)" >/dev/null \
  || fail "Transport Order $story_14 didn't reach DELIVERED in the Shipper's view in 10 minutes: $(step watch | jq -c '{statuses}')"
order='["SCHEDULED","COLLECTED","IN_TRANSIT","DELIVERED"]'
jq -e --argjson order "$order" '.statuses | map(. as $s | $order | index($s)) | . == sort and (map(. != null) | all)' <<<"$(step watch)" >/dev/null \
  || fail "the Shipper's view of Transport Order $story_14 moved backwards or showed an unknown status: $(step watch | jq -c '{statuses}')"
pass "the Shipper's view moved forward only, $(step watch | jq -r '.statuses | join(" > ")')"
jq -e '(.files | length > 0) and (.codes | last) == "DELIVERED"' <<<"$(step watch)" >/dev/null \
  || fail "the DELIVERED view of $story_14 has no Proof of delivery file, or no Delivered event: $(step watch | jq -c '{files, codes}')"
jq -e '.httpStatus == 200 and .bytes > 0' <<<"$(step download)" >/dev/null \
  || fail "the sample CMR didn't download through API Management: $(step download | jq -c '{httpStatus, contentType, bytes}')"
pass "the sample CMR downloads through API Management ($(step download | jq -r .contentType), $(step download | jq -r .bytes) bytes)"
detail_id=$(step watch | jq -r .correlationId)
confirm "Did the screen move from Scheduled to Delivered by itself, and offer the sample CMR for download?" \
  || fail "the presenter didn't see Transport Order $story_14's screen move by itself to Delivered with the sample CMR"
pass "the presenter saw Brouwer Dranken's screen move from Scheduled to Delivered by itself and offer the sample CMR"

# 2. A Carrier Persona is refused.
run "$(jq -nc --arg n "$story_14" '[
  {name: "carrier-list", do: "get", as: "vandijk", path: "/transport-orders"},
  {name: "carrier-detail", do: "get", as: "vandijk", path: ("/transport-orders/" + $n)}]')" "$story_14"
for name in carrier-list carrier-detail; do
  [ "$(step "$name" | jq -r .httpStatus)" = 403 ] \
    || fail "a Carrier Persona's token against the tracking API ($name) didn't get 403: $(step "$name" | jq -c '{httpStatus}')"
done
refused_id=$(step carrier-list | jq -r .correlationId)
pass "Van Dijk Transport's token against the tracking API gets 403 Forbidden (correlation ID $refused_id)"

# 3. Story 15.
run "$(jq -nc --arg n "$story_15" '[
  {name: "before", do: "get", as: "brouwer", path: ("/transport-orders/" + $n)},
  {name: "PU", do: "send", code: "PU", offset: 0},
  {name: "DEP", do: "send", code: "DEP", offset: 10},
  {name: "in-transit", do: "watch", number: $n, seconds: 300, until: "IN_TRANSIT"}]')" "$story_15"
[ "$(step before | jq -r .status)" = SCHEDULED ] \
  || fail "Transport Order $story_15 isn't SCHEDULED (Booked, not tracked yet): $(step before | jq -c '{httpStatus, status}')"
jq -e --arg id "$van_dijk_id" '.carrierId == $id' <<<"$(step PU)" >/dev/null \
  || fail "Transport Order $story_15 isn't carried by Van Dijk Transport with a Confirmed Carrier booking: $(step PU | jq -c '{carrierId, httpStatus}')"
for code in PU DEP; do
  [ "$(step "$code" | jq -r .httpStatus)" = 202 ] || fail "Van Dijk Transport's $code didn't get 202 Accepted: $(step "$code" | jq -c '{httpStatus}')"
done
jq -e '(.statuses | last) == "IN_TRANSIT"' <<<"$(step in-transit)" >/dev/null \
  || fail "the Shipper doesn't see Transport Order $story_15 IN_TRANSIT after PU and DEP: $(step in-transit | jq -c '{statuses}')"
pass "PU and DEP returned 202 Accepted and Brouwer Dranken sees Transport Order $story_15 as IN_TRANSIT"
echo
echo "Open Brouwer Dranken's tracking screen for Transport Order $story_15: it says In transit."
echo "  $portal/personas/brouwer-dranken/tracking/$story_15"
read -r -p "Press Enter when it is open... "
# DLV is outside the mapping table, so the Carrier-tracking Logic App's run fails, as P5's presenter-panel fault makes it.
run "$(jq -nc --arg n "$story_15" '[
  {name: "before", do: "watch", number: $n, seconds: 1, until: null},
  {name: "DLV", do: "send", code: "DLV", offset: 20},
  {name: "after", do: "watch", number: $n, seconds: 90, until: null}]')" "$story_15"
[ "$(step DLV | jq -r .httpStatus)" = 202 ] \
  || fail "the Carrier's unmapped DLV didn't get 202 Accepted: $(step DLV | jq -c '{httpStatus}')"
pass "the Carrier's DLV, a code outside the mapping table, returned 202 Accepted"
jq -e '.statuses == ["IN_TRANSIT"] and (.lastUpdates | length) == 1 and .lastUpdates[0] != null' <<<"$(step after)" >/dev/null \
  || fail "the Shipper's view of $story_15 changed after the unmapped DLV: $(step after | jq -c '{statuses, lastUpdates}')"
jq -e --argjson before "$(step before | jq -c .lastUpdates)" '.lastUpdates == $before' <<<"$(step after)" >/dev/null \
  || fail "the last update of $story_15 moved after the unmapped DLV: $(step after | jq -c '{lastUpdates}')"
pass "90 seconds after the DLV the Shipper still sees IN_TRANSIT, last update $(step after | jq -r '.lastUpdates[0]') (unchanged)"
confirm "Does the screen stay on In transit, with its last update getting older?" \
  || fail "the presenter didn't see Transport Order $story_15's screen stay on In transit with an ageing last update"
pass "the presenter saw the screen stay on In transit with its last update getting older"
stuck_id=$(step after | jq -r .correlationId)

# 4. With the TMS stopped for API Management, the query fails at once, with a correlation ID.
echo
echo "Pointing API Management's tms-api backend away from the TMS..."
restore_tms=yes
set_backend "https://tms-api.stopped.invalid"
sleep 60
run "$(jq -nc --arg n "$story_15" '[{name: "stopped", do: "get", as: "brouwer", path: ("/transport-orders/" + $n)}]')" "$story_15"
[ "$(step stopped | jq -r .httpStatus)" = 503 ] \
  || fail "a query with the TMS stopped didn't get 503: $(step stopped | jq -c '{httpStatus}')"
stopped_id=$(step stopped | jq -r '.correlationId // empty')
[ -n "$stopped_id" ] || fail "API Management's 503 carries no correlation ID"
pass "a query with the TMS stopped returns 503 with correlation ID $stopped_id"
echo "Look at Brouwer Dranken's screen (it polls every few seconds): it shows the error, and when its last answer was fetched."
confirm "Does the screen keep its last answer, show the error and show the time it was fetched?" \
  || fail "the presenter didn't see the error with the time of the last answer on Transport Order $story_15's screen"
pass "the presenter saw the error with the time of the last answer"
set_backend "$backend_url"
restore_tms=
sleep 60
run "$(jq -nc --arg n "$story_15" '[{name: "back", do: "get", as: "brouwer", path: ("/transport-orders/" + $n)}]')" "$story_15"
[ "$(step back | jq -r .httpStatus)" = 200 ] \
  || fail "the query doesn't work again after the TMS is back: $(step back | jq -c '{httpStatus}')"
pass "the query works again once the TMS is back"

# 5. Each query by its correlation ID. Azure Monitor names the TMS after its Container Apps environment too.
roles_with() {
  logs "union AppRequests, AppTraces, AppDependencies
| where tostring(Properties) contains '$1'
| summarize by AppRoleName" | jq -r '.[][0]'
}
found() {
  local what=$1 value=$2 roles missing
  shift 2
  for _ in $(seq 1 30); do
    roles=$(roles_with "$value")
    missing=
    for role in "$@"; do
      grep -qi "$role" <<<"$roles" || missing="$missing $role"
    done
    if [ -z "$missing" ]; then
      pass "$what is in Application Insights: $(tr '\n' ' ' <<<"$roles")"
      return
    fi
    sleep 20
  done
  fail "$what isn't in Application Insights for$missing after 10 minutes"
}
found "the Transport Order query, by correlation ID $detail_id," "$detail_id" "$AZURE_ESTATE_API_MANAGEMENT" tms-api
found "the Carrier's refused query, by correlation ID $refused_id," "$refused_id" "$AZURE_ESTATE_API_MANAGEMENT"
found "the query of the stuck Transport Order, by correlation ID $stuck_id," "$stuck_id" "$AZURE_ESTATE_API_MANAGEMENT" tms-api
found "the failed query with the TMS stopped, by correlation ID $stopped_id," "$stopped_id" "$AZURE_ESTATE_API_MANAGEMENT"

console="https://$(az containerapp show --name integration-console-web --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn --output tsv)"
echo
echo "Open in the Partner portal:"
echo "  Story 14: $portal/personas/brouwer-dranken/tracking/$story_14"
echo "  Story 15: $portal/personas/brouwer-dranken/tracking/$story_15"
echo "Open in the Integration console, by each correlation ID (the unmapped DLV is on the carrier-tracking dead-letter queue; discard it there afterwards):"
echo "  Transport Order query: $console/?key=$detail_id"
echo "  Refused query: $console/?key=$refused_id"
echo "  Stuck Transport Order query: $console/?key=$stuck_id"
echo "  Query with the TMS stopped: $console/?key=$stopped_id"
