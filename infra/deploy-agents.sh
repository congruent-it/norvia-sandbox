#!/usr/bin/env bash
# azd postdeploy hook, last: publishes the Agents to Foundry from their definition files (ADR 0040), after the apps are deployed.
#
#   infra/deploy-agents.sh [<agent name>...]
#
# No names publishes every agents/*.agent.yaml. For each Agent it
#   1. points the Agent's MCP server at the application's address in Azure (the Integration operations agent's is the Integration console's,
#      which the estate could not know when it was deployed): the API Management backend, and ESTATE_INTEGRATION_CONSOLE_BASE_URL so
#      deploying the estate again keeps it;
#   2. makes the project's connection to the MCP server, with the agent-identity authentication type (infra/create-agent-connection.sh);
#   3. creates the Agent's next version as a prompt agent in the Foundry project, from the file: the model, the instructions, the temperature,
#      the MCP server as its one tool (read-only, no approval to ask for) and the correlation ID as a structured input that the tool's
#      X-Correlation-ID header carries, so the Agent's tool calls join the Message trail it was asked about;
#   4. reads the Agent's identity (Foundry gives each Agent its own) and writes .azure/<env>/published-agents.json;
#   5. gives each identity its one read role (infra/assign-agent-roles.sh), which a new identity needs after every publish.
# It prints names, versions and counts only: never a token. Publishing the same file again is a new version of the same Agent.
set -euo pipefail

eval "$(azd env get-values)"
repo=$(cd "$(dirname "$0")/.." && pwd)
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
endpoint=${FOUNDRY_PROJECT_ENDPOINT:-${AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT:-}}
published="$repo/.azure/${AZURE_ENV_NAME}/published-agents.json"
gateway=${AZURE_ESTATE_PARTNER_API_URL%/partner/v1}
api_version=v1

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

for tool in yq jq curl az; do
  command -v "$tool" >/dev/null || fail "$tool is not installed"
done
[ -n "$endpoint" ] || fail "no Foundry project endpoint: has the estate been deployed (azd provision)?"
[ -n "$gateway" ] || fail "no API Management address: has the estate been deployed (azd provision)?"

if [ $# -gt 0 ]; then
  files=()
  for name in "$@"; do files+=("$repo/agents/$name.agent.yaml"); done
else
  files=("$repo"/agents/*.agent.yaml)
fi

# foundry METHOD PATH [BODY]: prints the response body; on an error prints the status and Foundry's error code and stops.
foundry() {
  local method=$1 path=$2 body=${3:-} out status
  out=$(mktemp)
  status=$(curl -s -o "$out" -w '%{http_code}' --max-time 120 -X "$method" "$endpoint$path" \
    -H "Authorization: Bearer $(az account get-access-token --resource https://ai.azure.com --query accessToken --output tsv)" \
    -H 'Content-Type: application/json' ${body:+-d "$body"})
  if [ "${status:0:1}" != 2 ]; then
    echo "Foundry $method $path answered $status: $(jq -r '.error.code // .error // "no-detail"' "$out" 2>/dev/null)" >&2
    rm -f "$out"
    return 1
  fi
  cat "$out"
  rm -f "$out"
}

# 1. Point each server's backend at its application. Only the Integration operations agent's server has one today.
servers=$(for file in "${files[@]}"; do yq -r '.tools.server' "$file"; done | sort -u)
apim=$(az resource list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" --resource-type Microsoft.ApiManagement/service \
  --query '[0].name' --output tsv)
[ -n "$apim" ] || fail "there is no API Management service in $resource_group"
for server in $servers; do
  [ "$server" = integration-operations ] || continue
  console="https://$(az containerapp show --subscription "$AZURE_SUBSCRIPTION_ID" --name integration-console-web --resource-group "$resource_group" \
    --query properties.configuration.ingress.fqdn --output tsv)"
  azd env set ESTATE_INTEGRATION_CONSOLE_BASE_URL "$console"
  az rest --method patch --output none \
    --url "https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$resource_group/providers/Microsoft.ApiManagement/service/$apim/backends/integration-console-agent-api?api-version=2024-05-01" \
    --body "$(jq -n --arg url "$console/agent/v1" '{properties: {url: $url}}')"
  pass "the $server server calls the Integration console at $console/agent/v1"
done

# 2. The connections.
# shellcheck disable=SC2086 # one argument per server
"$repo/infra/create-agent-connection.sh" $servers

# 3 and 4. Publish.
deployments=$(az cognitiveservices account deployment list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" \
  --name "$(az cognitiveservices account list --subscription "$AZURE_SUBSCRIPTION_ID" --resource-group "$resource_group" --query "[?kind=='AIServices'].name | [0]" --output tsv)" \
  --query '[].name' --output json)
mkdir -p "$(dirname "$published")"
results=()
for file in "${files[@]}"; do
  [ -f "$file" ] || fail "there is no definition file $file"
  definition=$(yq -o=json '.' "$file")
  name=$(jq -r .name <<<"$definition")
  [ "$(basename "$file")" = "$name.agent.yaml" ] || fail "$file names the Agent '$name'"
  server=$(jq -r .tools.server <<<"$definition")
  header=$(jq -r .inputs.correlationIdHeader <<<"$definition")
  deployment=$(jq -r .model.deployment <<<"$definition")
  if ! jq -e --arg d "$deployment" 'index($d)' <<<"$deployments" >/dev/null; then
    deployment=$(jq -r '.[0] // empty' <<<"$deployments")
    [ -n "$deployment" ] || fail "the Foundry account has no model deployment (foundryModelDeployed is off?)"
    echo "NOTE  $name: its model deployment is not in the project; using the estate's $deployment"
  fi

  body=$(jq -n --argjson d "$definition" --arg deployment "$deployment" --arg url "$gateway/agents/$server/mcp" --arg header "$header" '{
    description: $d.description,
    metadata: {definitionVersion: ($d.version | tostring)},
    definition: {
      kind: "prompt",
      model: $deployment,
      instructions: $d.instructions,
      temperature: $d.model.temperature,
      tools: [{
        type: "mcp",
        server_label: ($d.tools.server | gsub("-"; "_")),
        server_url: $url,
        require_approval: "never",
        project_connection_id: ($d.tools.server + "-mcp"),
        allowed_tools: $d.tools.names,
        headers: {($header): "{{correlationId}}"}
      }],
      structured_inputs: {
        correlationId: {description: "The correlation ID of the Message trail in question, sent on every tool call.", required: true, schema: {type: "string"}}
      }
    }
  }')
  version=$(foundry POST "/agents/$name/versions?api-version=$api_version" "$body" | jq -r .version)
  [ -n "$version" ] && [ "$version" != null ] || fail "$name: Foundry made no version"
  pass "$name: version $version (definition version $(jq -r .version <<<"$definition")) is published"

  # Foundry gives the Agent its own identity (the project's shared identity holds no role, so an Agent without one can call nothing).
  identity=$(foundry GET "/agents/$name?api-version=$api_version" | jq -r '.instance_identity.principal_id // .identity.principal_id // empty')
  [ -n "$identity" ] || fail "$name: Foundry gave the Agent no identity of its own"
  results+=("$(jq -n --arg agent "$name" --arg server "$server" --arg id "$identity" '{agent: $agent, server: $server, agentIdentityId: $id}')")
done
printf '%s\n' "${results[@]}" | jq -s . > "$published"

# 5. The roles.
"$repo/infra/assign-agent-roles.sh" "$published"
