#!/bin/sh
# Sets the estate's as-found toggles and deploys the estate with them (docs/operations/as-found.md).
#
#   infra/as-found.sh fixed                                  every alert, and a receiver
#   infra/as-found.sh as-found                               the preset in infra/presets/as-found.json
#   infra/as-found.sh noAlertReceiver=true                   one toggle, the others as they are
#   infra/as-found.sh flowsWithoutFailedRunAlert=a,b         a list toggle takes a comma-separated list, empty for none
#
# It stores the toggles in the azd environment as ESTATE_ASFOUND, runs the estate deployment alone (azd's preprovision
# hook, not the apps), deploys again the Logic Apps whose workflow a toggle changes (tracked properties, the Driver app report),
# sets the TMS Container App's settings for the business safety-net toggles, and prints what changed. The TMS's
# lifecycle rule (proofFilesStayHot) is Aspire's, so it changes at the next azd provision; the script says so.
set -eu
cd "$(dirname "$0")"

usage() {
  echo "Usage: infra/as-found.sh <preset|toggle=value>" >&2
  echo "Presets: $(ls presets | sed 's/\.json$//' | tr '\n' ' ')" >&2
  echo "Toggles: $(jq -r 'keys | join(" ")' presets/fixed.json)" >&2
  exit 2
}

[ $# -eq 1 ] || usage

current=$(azd env get-value ESTATE_ASFOUND 2>/dev/null || true)
[ -n "$current" ] || current=$(cat presets/fixed.json)
current=$(printf '%s' "$current" | jq -S -c .)

case "$1" in
  *=*)
    toggle=${1%%=*}
    value=${1#*=}
    kind=$(jq -r --arg toggle "$toggle" '.[$toggle] | type' presets/fixed.json)
    case "$kind" in
      boolean)
        [ "$value" = true ] || [ "$value" = false ] || { echo "$toggle takes true or false." >&2; exit 2; }
        updated=$(printf '%s' "$current" | jq -S -c --arg toggle "$toggle" --argjson value "$value" '.[$toggle] = $value')
        ;;
      array)
        updated=$(printf '%s' "$current" | jq -S -c --arg toggle "$toggle" --arg value "$value" \
          '.[$toggle] = ($value | split(",") | map(select(. != "")))')
        ;;
      *) echo "$toggle is not a toggle." >&2; usage ;;
    esac
    ;;
  *)
    [ -f "presets/$1.json" ] || { echo "$1 is not a preset." >&2; usage; }
    updated=$(jq -S -c . "presets/$1.json")
    ;;
esac

azd env set ESTATE_ASFOUND "$updated"
echo "Deploying the estate with the as-found toggles..."
azd hooks run preprovision

flows_changed=$(jq -n -r --argjson before "$current" --argjson after "$updated" \
  '((($before.logicAppsWithoutTrackedProperties // []) - ($after.logicAppsWithoutTrackedProperties // [])) + (($after.logicAppsWithoutTrackedProperties // []) - ($before.logicAppsWithoutTrackedProperties // [])))[]')
if [ "$(printf '%s' "$current" | jq '.noReportBackToDriverApp // false')" != "$(printf '%s' "$updated" | jq '.noReportBackToDriverApp // false')" ]; then
  flows_changed="$flows_changed
driver-reports"
fi
if [ -n "$flows_changed" ]; then
  # The tracked properties are in the workflow, which the deploy scripts publish (infra/stage-logic-app.ps1).
  repository=$(cd .. && pwd)
  for flow in $flows_changed; do
    script=$(grep -lE " $flow( lib)?\$" deploy-*-logic-app.sh | head -n 1 || true)
    [ -n "$script" ] || { echo "No deploy script for the flow $flow." >&2; exit 1; }
    echo "Deploying the $flow Logic App again ($script)..."
    (cd "$repository" && eval "$(azd env get-values | sed 's/^/export /')" && "infra/$script")
  done
fi
# The business safety nets are settings of the TMS's Container App. Aspire sets them at the next deploy from the same toggles; this applies
# them to the running app now, as a new revision.
settings=$(jq -n -r --argjson before "$current" --argjson after "$updated" '
  {staleThresholdsUnset: "ShipmentStale__ThresholdsSet", proofMissingThresholdUnset: "ProofsOfDelivery__MissingThresholdSet", lateDeliveryCheckOff: "LateDelivery__CheckEnabled"}
  | to_entries[] | select(($before[.key] // false) != ($after[.key] // false)) | "\(.value)=\(if ($after[.key] // false) then "false" else "true" end)"')
if [ -n "$settings" ]; then
  eval "$(azd env get-values | sed 's/^/export /')"
  echo "Setting the TMS API's business safety nets..."
  # shellcheck disable=SC2086
  az containerapp update --subscription "$AZURE_SUBSCRIPTION_ID" --name tms-api --resource-group "${AZURE_RESOURCE_GROUP:-rg-$AZURE_ENV_NAME}" \
    --set-env-vars $settings --output none
fi
if [ "$(printf '%s' "$current" | jq '.proofFilesStayHot // false')" != "$(printf '%s' "$updated" | jq '.proofFilesStayHot // false')" ]; then
  echo "proofFilesStayHot changed: the TMS's storage account is Aspire's, so run azd provision to apply it."
fi

echo "Changed:"
diff=$(jq -n -r --argjson before "$current" --argjson after "$updated" \
  '$after | to_entries[] | select($before[.key] != .value) | "  \(.key): \($before[.key] | tojson) -> \(.value | tojson)"')
echo "${diff:-  nothing, the toggles were already set}"
echo "Now:"
printf '%s' "$updated" | jq -r 'to_entries[] | "  \(.key): \(.value | tojson)"'
