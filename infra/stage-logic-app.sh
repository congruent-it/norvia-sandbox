#!/bin/sh
# Zips a Logic App project for deployment (infra/deploy-*-logic-app.sh), leaving the tracked properties out of the workflows that
# the as-found toggle logicAppsWithoutTrackedProperties lists (docs/operations/as-found.md). The sources are never changed.
#
#   infra/stage-logic-app.sh <project directory> <zip file> <files and folders to include>...
#
# The toggle is read from ESTATE_ASFOUND, which azd keeps in its environment; unset is the fixed state.
set -eu

project=$1
zip_file=$2
shift 2

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
cp -R "$project/." "$work/"

listed=
if [ -n "${ESTATE_ASFOUND:-}" ]; then
  listed=$(printf '%s' "$ESTATE_ASFOUND" | jq -r '(.logicAppsWithoutTrackedProperties // [])[]')
fi
for flow in $listed; do
  if [ -f "$work/$flow/workflow.json" ]; then
    jq 'walk(if type == "object" then del(.trackedProperties) else . end)' "$work/$flow/workflow.json" > "$work/$flow/workflow.json.new"
    mv "$work/$flow/workflow.json.new" "$work/$flow/workflow.json"
    echo "Leaving the tracked properties out of $flow (as-found)."
  fi
done

rm -f "$zip_file"
(cd "$work" && zip -qr "$zip_file" "$@")
