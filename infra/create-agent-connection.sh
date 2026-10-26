#!/usr/bin/env bash
# azd postdeploy hook: the Foundry project's connection to an API Management MCP server, with the agent-identity authentication
# type and the server's audience. Foundry then calls the server with the token of the Agent that makes the call (its own identity,
# made when it is published), never the project's shared identity.
#
#   infra/create-agent-connection.sh <server> [<server>...]
#
# A server is the name in its contract (contracts/agent-mcp/<server>.tools.json). The connection is named "<server>-mcp"; the
# target is the server's address on the gateway, and the audience is the agent view's app registration ("Norvia <server> agent
# view (<env>)"). Running it again updates the connection.
#
# Why a script: the Bicep connection resource (Microsoft.CognitiveServices/accounts/projects/connections, API versions up to
# 2026-07-01) lists no agent-identity authType (AAD, AccessKey, AccountKey, ApiKey, CustomKeys, ManagedIdentity, None, OAuth2, PAT,
# SAS, ServicePrincipal, UsernamePassword only), so Bicep cannot make this connection (docs/operations/foundry.md). When it can,
# delete this script and use Bicep.
set -euo pipefail

[ $# -ge 1 ] || { echo "Usage: $0 <server> [<server>...]" >&2; exit 2; }
eval "$(azd env get-values)"
repo=$(cd "$(dirname "$0")/.." && pwd)
endpoint=${FOUNDRY_PROJECT_ENDPOINT:-${AZURE_ESTATE_FOUNDRY_PROJECT_ENDPOINT:-}}
[ -n "$endpoint" ] || { echo "No Foundry project endpoint: has the estate been deployed (azd provision)?" >&2; exit 1; }
gateway=${AZURE_ESTATE_PARTNER_API_URL%/partner/v1}

azd ai project set "$endpoint" >/dev/null

for server in "$@"; do
  [ -f "$repo/contracts/agent-mcp/$server.tools.json" ] || { echo "No contract for the server $server." >&2; exit 1; }
  audience=$(az ad app list --display-name "Norvia $server agent view ($AZURE_ENV_NAME)" --query '[0].appId' --output tsv)
  [ -n "$audience" ] || { echo "The agent view of $server is not in Entra: has the estate been deployed?" >&2; exit 1; }
  azd ai connection create "$server-mcp" --kind remote-tool --target "$gateway/agents/$server/mcp" \
    --auth-type agentic-identity --audience "$audience" --force
  echo "Connection $server-mcp: $gateway/agents/$server/mcp (audience $audience)"
done
