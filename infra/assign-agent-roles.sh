#!/usr/bin/env bash
# azd postdeploy hook, after every publish (infra/deploy-agents.sh): gives each published Agent's identity the one read role of its
# API, an app role on the API's app registration, and checks that the project's shared identity holds none (ADR 0040). Foundry
# makes a new identity on every publish, so the role is assigned again every time. The superseded identity keeps its role until
# Foundry deletes it with the version (docs/operations/foundry.md, "When a publish gives a new identity").
#
#   infra/assign-agent-roles.sh [published-agents.json]
#
# The input is what deploy-agents.sh writes after publishing, one entry per Agent:
#   [{ "agent": "integration-operations", "server": "example", "agentIdentityId": "<object ID of the agent identity>" }]
# The server names a contract (contracts/agent-mcp/<server>.tools.json), which holds the role. The audience, the API's app
# registration, is "Norvia <server> agent view (<env>)"; the example's is "Norvia example agent view (<env>)".
#
# Which credential: the Graph call needs AppRoleAssignment.ReadWrite.All and Application.Read.All. The signed-in `az` token is used
# unless AGENT_ROLES_CLIENT_ID is set, in which case the deployment app registration signs in with client credentials (a secret in
# AGENT_ROLES_CLIENT_SECRET, or a federated credential's assertion in AGENT_ROLES_CLIENT_ASSERTION in CI). Why, and what is known
# and not: docs/operations/foundry.md, "Agent roles: which credential". It prints names and counts only, never a token.
set -euo pipefail

eval "$(azd env get-values)"
repo=$(cd "$(dirname "$0")/.." && pwd)
published=${1:-${AGENT_PUBLISH_RESULT:-$repo/.azure/${AZURE_ENV_NAME}/published-agents.json}}
[ -f "$published" ] || { echo "No published agents at $published: run infra/deploy-agents.sh first." >&2; exit 1; }
tenant=$(az account show --query tenantId --output tsv)
graph=https://graph.microsoft.com/v1.0

graph_token() {
  if [ -n "${AGENT_ROLES_CLIENT_ID:-}" ]; then
    local assertion=()
    if [ -n "${AGENT_ROLES_CLIENT_ASSERTION:-}" ]; then
      assertion=(--data-urlencode "client_assertion=$AGENT_ROLES_CLIENT_ASSERTION"
        --data-urlencode client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer)
    else
      assertion=(--data-urlencode "client_secret=${AGENT_ROLES_CLIENT_SECRET:?AGENT_ROLES_CLIENT_SECRET or AGENT_ROLES_CLIENT_ASSERTION}")
    fi
    curl -sf -X POST "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token" \
      --data-urlencode "client_id=$AGENT_ROLES_CLIENT_ID" "${assertion[@]}" \
      --data-urlencode scope=https://graph.microsoft.com/.default --data-urlencode grant_type=client_credentials | jq -r .access_token
  else
    az account get-access-token --resource-type ms-graph --query accessToken --output tsv
  fi
}
token=$(graph_token)

# graph METHOD PATH [BODY]: prints the response body; on an error prints Graph's error code and stops. Never prints the token.
graph() {
  local method=$1 path=$2 body=${3:-} out status
  out=$(mktemp)
  # curl refuses a URL with a space in it (an app registration's display name has some), so they are encoded.
  status=$(curl -s -o "$out" -w '%{http_code}' -X "$method" "$graph${path// /%20}" -H "Authorization: Bearer $token" \
    -H 'Content-Type: application/json' ${body:+-d "$body"})
  if [ "${status:0:1}" != 2 ]; then
    echo "Graph $method $path answered $status: $(jq -r '.error.code + ": " + .error.message' "$out" 2>/dev/null || echo no-detail)" >&2
    rm -f "$out"
    return 1
  fi
  cat "$out"
  rm -f "$out"
}

pass() { echo "PASS  $*"; }
fail() { echo "FAIL  $*" >&2; exit 1; }

assigned=0
while read -r entry; do
  agent=$(jq -r .agent <<<"$entry")
  server=$(jq -r .server <<<"$entry")
  identity=$(jq -r .agentIdentityId <<<"$entry")
  role=$(jq -r .role "$repo/contracts/agent-mcp/$server.tools.json")

  view_name="Norvia $server agent view ($AZURE_ENV_NAME)"
  view=$(graph GET "/servicePrincipals?\$filter=displayName eq '$view_name'&\$select=id,appRoles")
  view_id=$(jq -r '.value[0].id // empty' <<<"$view")
  [ -n "$view_id" ] || fail "$agent: the service principal '$view_name' is not in Entra: has the estate been deployed?"
  role_id=$(jq -r --arg role "$role" '.value[0].appRoles[] | select(.value == $role) | .id' <<<"$view")
  [ -n "$role_id" ] || fail "$agent: '$view_name' has no app role $role"

  # The identity's assignments on this view. Another identity of the same Agent (an earlier publish) is removed below.
  current=$(graph GET "/servicePrincipals/$view_id/appRoleAssignedTo?\$top=999")
  if jq -e --arg id "$identity" --arg role "$role_id" '.value[] | select(.principalId == $id and .appRoleId == $role)' <<<"$current" >/dev/null; then
    pass "$agent ($identity) already holds $role"
  else
    graph POST "/servicePrincipals/$view_id/appRoleAssignedTo" \
      "$(jq -n --arg p "$identity" --arg r "$view_id" --arg a "$role_id" '{principalId: $p, resourceId: $r, appRoleId: $a}')" >/dev/null ||
      fail "$agent: the role assignment was refused. See docs/operations/foundry.md, 'Agent roles: which credential'"
    pass "$agent ($identity) now holds $role"
  fi
  assigned=$((assigned + 1))
done < <(jq -c '.[]' "$published")

# The project's shared identity holds no role on any agent view: an unpublished Agent can call nothing.
resource_group=${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}
account=$(az cognitiveservices account list --resource-group "$resource_group" --query "[?kind=='AIServices'].name | [0]" --output tsv)
project_identity=$(az resource show --resource-group "$resource_group" --resource-type Microsoft.CognitiveServices/accounts/projects \
  --name "$account/norvia" --query identity.principalId --output tsv)
if [ -n "$project_identity" ]; then
  held=$(graph GET "/servicePrincipals/$project_identity/appRoleAssignments" | jq '.value | length')
  [ "$held" = 0 ] || fail "the project's shared identity $project_identity holds $held app role(s): it must hold none"
  pass "the project's shared identity holds no app role"
fi

echo "$assigned Agent(s) hold their read role."
