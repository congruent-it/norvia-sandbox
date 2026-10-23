# The as-found estate

The sandbox exists to reproduce what an integration health check finds, and to fix it one problem at a time ([SCENARIO.md](../../SCENARIO.md)). The estate is built healthy. A **toggle** makes it as it was found: it removes something that exists. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

So far the toggles cover the estate's alerts: a failed-run alert for every Logic App and a dead-letter alert for every queue and subscription a Logic App reads. The headline incident is Carrier tracking (P5): the tracking Logic App fails and the tracking queue dead-letters, and nobody is told. [failure-behaviour.md](../architecture/failure-behaviour.md) names each hop's alert and toggle.

## The alerts

| Alert | Where | Rule |
|---|---|---|
| Failed runs | One per Logic App, `alert-failed-runs-<workflow>` | A metric alert on the Logic App's `WorkflowRunsCompleted`, filtered to the flow's workflow and the status `Failed`: any failed run in 5 minutes |
| Dead letter | One per queue and per subscription a Logic App reads, `alert-dead-letter-<entity>` | A metric alert on the Service Bus namespace's `DeadletteredMessages`, filtered to the entity (`EntityName` is the queue's or subscription's name): any message in its dead-letter queue. It stays until the queue is emptied |

Both go to one **action group**, which emails the address in `ESTATE_ALERT_EMAIL` (`azd env set ESTATE_ALERT_EMAIL <address>`). Nothing is built into the Integration console: it neither raises nor shows alerts.

Metric alerts, not scheduled query rules: both signals are platform metrics, so a rule needs no query, no workspace and no ingestion delay, and a Logic App and a queue are filtered by a dimension. A scheduled query rule would be the choice for a signal that exists only as a log, such as a failure code in a trace.

The rules are in [alerts.bicep](../../infra/estate/alerts.bicep) and the action group in [monitoring.bicep](../../infra/estate/monitoring.bicep). One loop over the estate's Logic Apps and one over its queues and subscriptions build the rules. Each Logic App runs one workflow, so the workflow's name (`carrier-booking`, `billing`) is the name the alert and the toggles use. Subscription names are unique among the alerted ones, because a metric cannot tell `monitoring` on one topic from `monitoring` on another.

### The drift guard

[AlertDriftGuardTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AlertDriftGuardTests.cs) reads the Bicep under [infra/estate](../../infra/estate) and fails when a Logic App or a queue or subscription has neither an alert in the `alerts` module of [estate.bicep](../../infra/estate.bicep) nor an exemption, so a new flow cannot be added unalerted by accident. An exemption is an entry with its reason in [alert-exemptions.json](../../infra/estate/alert-exemptions.json). Today the only ones are the four `monitoring` subscriptions, which nothing reads. The test also fails on an exemption or an alert for something that no longer exists.

## Toggles

The toggles are one Bicep object, `asFound`, healthy by default:

| Toggle | Type | Effect |
|---|---|---|
| `flowsWithoutFailedRunAlert` | list of workflow names | A listed Logic App has no failed-run alert |
| `entitiesWithoutDeadLetterAlert` | list of queue and subscription names | A listed queue or subscription has no dead-letter alert |
| `noAlertReceiver` | true or false | The action group is built with no receiver: the alerts exist and go nowhere |

A "no alert" is an **absent** rule, never a disabled one: a health check finds nothing where a rule should be, and a disabled rule would be a different finding. Deploying never deletes, so [provision-estate.sh](../../infra/provision-estate.sh) deletes the rules a toggle now lists.

## Presets

A preset is a file in [infra/presets](../../infra/presets) holding a value of `asFound`.

| Preset | State |
|---|---|
| `fixed` | Every alert, and a receiver. Nothing broken. What the estate is without a toggle |
| `as-found` | A realistic partial set: six Logic Apps (the earlier-built Carrier flows, notifications and `billing`) and seven queues and subscriptions unalerted, the rest alerted. The incident flows are among the unalerted: `carrier-tracking`, `carrier-proof-of-delivery`, `billing` |

[estate.bicepparam](../../infra/estate.bicepparam) takes the object from the `ESTATE_ASFOUND` environment variable, as JSON, and the `fixed` preset when it is unset.

## The script

```sh
infra/as-found.sh <preset|toggle=value>
```

```sh
infra/as-found.sh as-found                              # a preset
infra/as-found.sh fixed
infra/as-found.sh noAlertReceiver=true                  # one toggle; the others stay as they are
infra/as-found.sh flowsWithoutFailedRunAlert=carrier-tracking,billing
infra/as-found.sh flowsWithoutFailedRunAlert=           # a list toggle, emptied
```

It stores the toggles in the azd environment (`ESTATE_ASFOUND`), deploys the estate alone (`azd hooks run preprovision`, not the apps) and prints which toggles changed and the state now. Toggles are Azure-only: locally there are no alerts. For a live demo, switch to `as-found` beforehand, and run `infra/as-found.sh fixed` live as the fix.

## Reading a result

- Open **Monitor > Alerts > Alert rules** in the estate's resource group. A flow or queue with a toggle has no rule of its name; one without has exactly one, enabled.
- Open the action group `ag-norvia-…`: with `noAlertReceiver` it lists no email receiver, though the rules exist.
- To see the incident, send a Tracking event the TMS refuses ([smoke-test-shipment-tracking.sh](../../infra/smoke-test-shipment-tracking.sh)): in `fixed` an email arrives within minutes; in `as-found` the message sits in the dead-letter queue until someone opens it in the Integration console.

## Testing

The template tests ([AsFoundAlertsTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AsFoundAlertsTests.cs)) evaluate `alerts.bicep` with `bicep snapshot` for each preset and assert which rules exist: a listed entity has none, an unlisted one exactly one, and none is ever disabled. The drift guard above is the third test class. After `azd up`, [smoke-test-as-found-alerts.sh](../../infra/smoke-test-as-found-alerts.sh) applies `as-found`, checks a sample of the rules is absent (tracking, booking and billing runs; the tracking, Proof of delivery and billing queues) and a sample is present (intake and Trip dispatch runs, the `driver-reports` subscription) with `az`, applies `fixed`, checks the whole sample exists and the action group has its receiver, then checks `noAlertReceiver`.
