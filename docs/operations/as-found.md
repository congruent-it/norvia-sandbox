# The as-found estate

The sandbox exists to reproduce what an integration health check finds, and to fix it one problem at a time ([SCENARIO.md](../../SCENARIO.md)). The estate is built healthy. A **toggle** makes it as it was found: it removes something that exists. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

So far the toggles cover the estate's alerts (a failed-run alert for every Logic App and a dead-letter alert for every queue and subscription a Logic App reads) and its [storage, secrets and retention](#storage-secrets-and-retention). The headline incident is Carrier tracking (P5): the tracking Logic App fails and the tracking queue dead-letters, and nobody is told. [failure-behaviour.md](../architecture/failure-behaviour.md) names each hop's alert and toggle.

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

## Storage, secrets and retention

The configuration problems a health check finds in storage, secrets and retention. Each is built healthy first, and a toggle in the same `asFound` object takes away just that setting; the others stay as they are.

| Toggle | Type | Healthy | Effect | From |
|---|---|---|---|---|
| `noInboundLifecycleRule` | true or false | The inbound Proof of delivery container (`stinbound…`) has a lifecycle rule that deletes a blob after `integrationRetentionDays` | The rule is absent: a blob a failed message left behind is never deleted, and storage grows silently | P6 |
| `noTransitLifecycleRule` | true or false | The Driver app's transit container (`stdriver…`) has the same kind of rule | The rule is absent: files that were refused or never fetched stay for good | P12 |
| `proofFilesStayHot` | true or false | The TMS's `proofs-of-delivery` container moves its files to the cool tier after 30 days | The rule is absent: the files stay in the hot tier, never archived or deleted either way | P6 |
| `noMalwareScanning` | true or false | Defender for Storage malware scanning is on for the inbound account, scanning each blob on upload | The setting is absent: files from outside reach the TMS unscanned | P6 |
| `carrierSecretInlined` | true or false | The booking Logic App reads every Carrier endpoint credential from Key Vault | The booking Logic App also holds the first Carrier's credential in clear text in its app settings (`CARRIER_ENDPOINT_KEY`). The workflow still reads from Key Vault, so nothing breaks: the finding is the copy | P4 |
| `appInsightsRetentionShort` | true or false | Application Insights keeps what it traced for `integrationRetentionDays`, as run history does | Application Insights keeps 30 days (never more than `integrationRetentionDays`); the workspace and run history keep their retention, so run bodies outlive the index that finds them | [Integration console](integration-console.md#retention) |
| `logicAppsWithoutTrackedProperties` | list of workflow names | Every workflow records its tracked properties | A listed workflow is deployed without them, so its trail can be found by correlation ID but not by Shipment number | [Integration console](integration-console.md) |

A lifecycle rule, the malware scanning and an alert are **absent**, never disabled, so [provision-estate.sh](../../infra/provision-estate.sh) deletes what a toggle now leaves out.

- **Malware scanning costs money.** Defender for Storage is billed per storage account per month, plus per GB scanned. The sandbox caps the scanning at 10 GB a month (`capGBPerMonth` in [inbound-storage.bicep](../../infra/estate/inbound-storage.bicep)), and a scan runs only on upload, so the fixed state stays cheap but not free. Check the current price on Microsoft's Defender for Storage pricing page before leaving it on. `noMalwareScanning` is also the cheap way to run the sandbox.
- **The secret is reported by where it is, never by its value.** A health check says "a Carrier credential is in the booking Logic App's settings", and the smoke run checks the setting by name. Nothing prints the value.
- **`proofFilesStayHot` is Aspire's.** The TMS's storage account is provisioned with the apps, so the AppHost reads the toggle from `ESTATE_ASFOUND`: `infra/as-found.sh` stores it and deletes the rule that exists, and every `azd provision` after that leaves the rule out. Going back to `fixed` needs an `azd provision` to build the rule again.
- **`logicAppsWithoutTrackedProperties` is a deployment.** The tracked properties are in the workflow, so [stage-logic-app.sh](../../infra/stage-logic-app.sh) leaves them out of the listed workflows when a deploy script zips the Logic App, and `as-found.sh` deploys the workflows whose listing changed. The sources are never changed.

## Presets

A preset is a file in [infra/presets](../../infra/presets) holding a value of `asFound`.

| Preset | State |
|---|---|
| `fixed` | Every alert, a receiver, and every storage, secret and retention protection. Nothing broken. What the estate is without a toggle |
| `as-found` | A realistic partial set. Alerts: six Logic Apps (the earlier-built Carrier flows, notifications and `billing`) and seven queues and subscriptions unalerted, the rest alerted. The incident flows are among the unalerted: `carrier-tracking`, `carrier-proof-of-delivery`, `billing`. Storage: no inbound lifecycle rule, files stay hot, no malware scanning; the transit rule is kept. A Carrier credential inlined, a short Application Insights retention, and no tracked properties on `billing`, `carrier-cancellation` and `shipper-notification` |

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
infra/as-found.sh noMalwareScanning=true
infra/as-found.sh logicAppsWithoutTrackedProperties=billing,carrier-cost
```

It stores the toggles in the azd environment (`ESTATE_ASFOUND`), deploys the estate alone (`azd hooks run preprovision`, not the apps), deploys again the Logic Apps whose tracked properties changed, and prints which toggles changed and the state now. Toggles are Azure-only: locally there are no alerts, lifecycle rules or scanning. For a live demo, switch to `as-found` beforehand, and run `infra/as-found.sh fixed` live as the fix.

## Reading a result

- Open **Monitor > Alerts > Alert rules** in the estate's resource group. A flow or queue with a toggle has no rule of its name; one without has exactly one, enabled.
- Open the action group `ag-norvia-…`: with `noAlertReceiver` it lists no email receiver, though the rules exist.
- Open the storage accounts: a toggle's account has no **Lifecycle management** rule (the inbound account's, the Driver app's transit account's, or the TMS's), and with `noMalwareScanning` the inbound account's **Microsoft Defender for Cloud** page has malware scanning off. The booking Logic App's **Environment variables** list `CARRIER_ENDPOINT_KEY` with `carrierSecretInlined`, and Application Insights' **Usage and estimated costs > Data retention** shows 30 days with `appInsightsRetentionShort`.
- To see the incident, send a Tracking event the TMS refuses ([smoke-test-shipment-tracking.sh](../../infra/smoke-test-shipment-tracking.sh)): in `fixed` an email arrives within minutes; in `as-found` the message sits in the dead-letter queue until someone opens it in the Integration console.

## Testing

The storage, secret and retention toggles have template tests of their own ([AsFoundStorageTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AsFoundStorageTests.cs)): per toggle, the healthy template gives the setting and the toggle removes only it (the tracked properties are checked on the zip [stage-logic-app.sh](../../infra/stage-logic-app.sh) makes), and the `proofFilesStayHot` rule is checked in the AppHost model tests. The alert template tests ([AsFoundAlertsTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AsFoundAlertsTests.cs)) evaluate `alerts.bicep` with `bicep snapshot` for each preset and assert which rules exist: a listed entity has none, an unlisted one exactly one, and none is ever disabled. The drift guard above is the third test class. After `azd up`, [smoke-test-as-found-alerts.sh](../../infra/smoke-test-as-found-alerts.sh) applies `as-found`, checks a sample of the rules is absent (tracking, booking and billing runs; the tracking, Proof of delivery and billing queues) and a sample is present (intake and Trip dispatch runs, the `driver-reports` subscription) with `az`, applies `fixed`, checks the whole sample exists and the action group has its receiver, then checks `noAlertReceiver`. It ends with one storage toggle and the Key Vault toggle: with `noInboundLifecycleRule` the inbound account has no lifecycle rule, and with `carrierSecretInlined` the booking Logic App has `CARRIER_ENDPOINT_KEY` (checked by name, the value never printed); under `fixed` neither.
