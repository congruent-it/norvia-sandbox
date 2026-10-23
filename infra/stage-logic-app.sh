#!/bin/sh
# Zips a Logic App project for deployment (infra/deploy-*-logic-app.sh), leaving the tracked properties out of the workflows that
# the as-found toggle logicAppsWithoutTrackedProperties lists, and replacing the calls that tell the Driver app what became of a report with
# an answer that says all is well when the toggle noReportBackToDriverApp is set (docs/operations/as-found.md). The sources are never changed.
#
#   infra/stage-logic-app.sh <project directory> <zip file> <files and folders to include>...
#
# The toggles are read from ESTATE_ASFOUND, which azd keeps in its environment; unset is the fixed state.
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

# No report back to the Driver app (P12): the report-outcomes calls are not made. Each becomes a step that answers 200 and does nothing, so
# the rest of the workflow, which reads the answer, runs as it does when the Driver app is well.
if [ -n "${ESTATE_ASFOUND:-}" ] && [ "$(printf '%s' "$ESTATE_ASFOUND" | jq -r '.noReportBackToDriverApp // false')" = true ] \
  && [ -f "$work/driver-reports/workflow.json" ]; then
  jq 'walk(if type == "object" and .type == "Http" and ((.inputs.uri // "") | contains("/v1/report-outcomes"))
        then .type = "Compose" | .inputs = {statusCode: 200} else . end)' "$work/driver-reports/workflow.json" > "$work/driver-reports/workflow.json.new"
  mv "$work/driver-reports/workflow.json.new" "$work/driver-reports/workflow.json"
  echo "Leaving out the reports to the Driver app (as-found)."
fi

rm -f "$zip_file"
(cd "$work" && zip -qr "$zip_file" "$@")
