# The as-found estate

The sandbox exists to reproduce what an integration health check finds, and to fix it one problem at a time ([SCENARIO.md](../../SCENARIO.md)). The estate is built healthy. A **toggle** makes it as it was found: it removes something that exists. Terms are defined in [GLOSSARY.md](../../GLOSSARY.md).

Besides the toggles there are [faults](#faults): runtime failures the presenter switches on for a few minutes from the Partner portal's presenter panel. They break something while it runs and put it back by themselves.

The [scenarios](#scenarios) combine presets and faults into named re-enactments, each with a smoke run. So far the toggles cover the estate's alerts (a failed-run alert for every Logic App and a dead-letter alert for every queue and subscription a Logic App reads), its [storage, secrets and retention](#storage-secrets-and-retention) and its [business safety nets and API Management](#business-safety-nets-and-api-management) and, for the second incident (the Azure bill), a [spend toggle](#spend). The headline incident is Carrier tracking (P5): the tracking Logic App fails and the tracking queue dead-letters, and nobody is told. [failure-behaviour.md](../architecture/failure-behaviour.md) names each hop's alert and toggle.

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

A "no alert" is an **absent** rule, never a disabled one: a health check finds nothing where a rule should be, and a disabled rule would be a different finding. Deploying never deletes, so [provision-estate.ps1](../../infra/provision-estate.ps1) deletes the rules a toggle now lists.

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

A lifecycle rule and an alert are **absent**, never disabled, so [provision-estate.ps1](../../infra/provision-estate.ps1) deletes what a toggle now leaves out. The malware scanning is the exception: Defender for Storage's setting cannot be deleted, so the script turns it off for the inbound account (the setting stays, with scanning and the account's Defender plan off), and clearing the toggle turns it on again.

- **Malware scanning costs money.** Defender for Storage is billed per storage account per month, plus per GB scanned. The sandbox caps the scanning at 10 GB a month (`capGBPerMonth` in [inbound-storage.bicep](../../infra/estate/inbound-storage.bicep)), and a scan runs only on upload, so the fixed state stays cheap but not free. Check the current price on Microsoft's Defender for Storage pricing page before leaving it on. `noMalwareScanning` is also the cheap way to run the sandbox.
- **The secret is reported by where it is, never by its value.** A health check says "a Carrier credential is in the booking Logic App's settings", and the smoke run checks the setting by name. Nothing prints the value.
- **`proofFilesStayHot` is Aspire's.** The TMS's storage account is provisioned with the apps, so the AppHost reads the toggle from `ESTATE_ASFOUND`: `infra/as-found.ps1` stores it and deletes the rule that exists, and every `azd provision` after that leaves the rule out. Going back to `fixed` needs an `azd provision` to build the rule again.
- **`logicAppsWithoutTrackedProperties` is a deployment.** The tracked properties are in the workflow, so [stage-logic-app.ps1](../../infra/stage-logic-app.ps1) leaves them out of the listed workflows when a deploy script zips the Logic App, and `as-found.ps1` deploys the workflows whose listing changed. The sources are never changed.

## Business safety nets and API Management

The business-side safety nets (P5, P6, P7) already exist as TMS settings; these toggles let the estate switch them. Together with a missing alert, only a Consignee's phone call reveals the problem. Each is built healthy first, and a toggle in the same `asFound` object changes just its own setting.

| Toggle | Type | Healthy | Effect | From |
|---|---|---|---|---|
| `staleThresholdsUnset` | true or false | The TMS flags a Shipment Stale from the thresholds `CollectionGrace`, `SilenceLimit` and `DeliveryGrace` | The thresholds are unset (`ShipmentStale__ThresholdsSet=false`): a quiet Shipment is never flagged Stale | P5 |
| `proofMissingThresholdUnset` | true or false | The Planner's list flags a Delivered Shipment's Proof of delivery missing after `MissingAfter` | The threshold is unset (`ProofsOfDelivery__MissingThresholdSet=false`): nothing is flagged missing | P6 |
| `lateDeliveryCheckOff` | true or false | The time-based check opens a Late delivery Exception once the delivery window plus `Grace` has passed | The check is off (`LateDelivery__CheckEnabled=false`): a passed window opens no Exception. A Late delivery from an ETA still opens one | P7 |
| `shipperTrackingCached` | true or false | The Shipper tracking operations have no cache | `list`, `find` and `get` Transport Order answer from API Management's cache, which keeps the answer for a day (86400 s), per Shipper and request. The TMS is right and the Shipper sees an old status | P8 |
| `noShipperRateLimit` | true or false | The four Shipper tracking operations share a rate limit of 120 calls a minute per subscription | The policies have no rate limit | P8 |
| `noReportBackToDriverApp` | true or false | The Driver reports Logic App tells the Driver app what became of each report | The calls to the Driver app are not made, so the report stays **Sending** in the Driver app although the TMS recorded it | P12 |

- **The three TMS toggles are settings of the TMS API's Container App.** The AppHost reads them from `ESTATE_ASFOUND` and sets the app's environment (`AsFound.TmsSettings` in [AsFound.cs](../../src/Norvia.AppHost/AsFound.cs)), healthy values included, so every `azd provision` or deploy keeps them. `infra/as-found.ps1` also applies a changed one to the running app at once (`az containerapp update`, a new revision). Thresholds are *unset*, not zero: a zero threshold would flag everything.
- **The cache and the rate limit are fragments of the policies.** The four Shipper tracking policies in [partner-api](../../infra/estate/partner-api) hold a place for each, and [partner-api.bicep](../../infra/estate/partner-api.bicep) fills it from [shipper-rate-limit.fragment.xml](../../infra/estate/partner-api/shipper-rate-limit.fragment.xml), [shipper-cache-lookup.fragment.xml](../../infra/estate/partner-api/shipper-cache-lookup.fragment.xml) and [shipper-cache-store.fragment.xml](../../infra/estate/partner-api/shipper-cache-store.fragment.xml), or with nothing. The cache key names the Shipper, so no Shipper is given another's answer. The answer is kept after it was mapped for the Shipper, and a hit is traced `Answered the Shipper from the cache` with outcome `Succeeded`. A file is never cached. Switching the toggle off leaves entries in the cache that nothing reads; they expire after a day.
- **`noReportBackToDriverApp` is a deployment.** [stage-logic-app.ps1](../../infra/stage-logic-app.ps1) turns each call to the Driver app's `/v1/report-outcomes` into a step that answers 200 and does nothing, so the rest of the workflow, which reads the answer, runs as it does when the Driver app is well. The hops for those steps therefore say `Succeeded`: the finding is the Driver app's list, not the trail. `as-found.ps1` deploys the Driver reports Logic App again when the toggle changes. The sources are never changed.

## Conversation sweeper

The Agent Service keeps conversations until they are deleted, and the Intake agent's hold a Shipper's email. The host deletes each run's conversation, and the nightly `conversation-sweeper` Logic App deletes anything older than a day ([operations/foundry.md](../operations/foundry.md#conversation-cleanup)). One toggle in the same `asFound` object takes the sweeper away:

| Toggle | Type | Healthy | Effect | From |
|---|---|---|---|---|
| `noConversationSweeper` | true or false | The sweeper runs every night | The sweeper Logic App is built but stopped (`enabled: false`), so it never runs. A conversation the host could not delete stays in the Foundry project, and the failed-run alert never fires because there is no run. A finding for the health check (#377) | Agents |

It is a deployment of the estate alone: `infra/as-found.ps1 noConversationSweeper=true` runs the estate's Bicep, and the workflow that was deployed stays. A disabled app cannot take a deployment, so the postdeploy hook skips the sweeper while the toggle is set; set it to false and run `azd deploy` (or `infra/deploy-conversation-sweeper-logic-app.ps1`) to deploy the workflow.

## Spend

The second incident is the Azure bill: Finance sees a rise with no matching change in shipment volume ([SCENARIO.md](../../SCENARIO.md)). A **spend toggle** raises the bill for no business reason. It is the one toggle that adds something, where the others take something away, and it is built healthy first like the rest.

| Toggle | Type | Healthy | Effect | From |
|---|---|---|---|---|
| `chattyTelemetry` | true or false | Every application logs at `Information` (`Logging__LogLevel__Default`) | Every application logs at `Debug`, so what the applications log grows with the background polling and everything the frameworks say. The Container Apps' console logs and the traces' logs flow into the estate's Log Analytics workspace, which is pay-per-GB (`PerGB2018`), on top of the unsampled traces of [ADR 0029](../adr/0029-the-applications-send-application-insights-every-trace-unsampled.md). The workspace's ingestion, and its bill, rises while the number of Transport Orders stays flat | [Cost view](#the-cost-shape) |

- **One compound cause.** A verbose log level and the workspace that bills it per GB. The workspace is pay-per-GB in the fixed estate too, because the Integration console and the Container Apps environment need it, so the toggle changes only the level. Nothing else is added or removed.
- **The AppHost sets the level.** `Norvia.AppHost` reads the toggle from `ESTATE_ASFOUND` and gives every application `Logging__LogLevel__Default`, `Information` or `Debug`; the healthy value is written out too, so going back to `fixed` changes it back. `infra/as-found.ps1` also sets it on the running Container Apps, a new revision each, so a toggle needs no deployment of the apps. The migrations are Container App jobs that run once per deploy: they take the level at the next one.
- **Azure only.** Locally nothing bills. The toggle is in every preset, `false`, and no preset turns it on: the health check's cost Check (#509) is its Check, and until it exists the Checks of the presets would find nothing for it. Turn it on with `infra/as-found.ps1 chattyTelemetry=true`.
- **Not measured yet.** How large the effect is against a sandbox bill, and so whether `Debug` is the right level and the pollers' interval the right one, needs an Azure deployment and a few days of Cost Management data. The level is `Debug` until that measurement says otherwise; the polling intervals are unchanged. Record the measurement here, and replace the numbers of the snapshot below with it.

### The cost shape

Locally there is no Cost Management, so the cost tools read a snapshot, as the health check does. Two files stand in for the bill, in [Snapshots](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots): [fixed.cost-snapshot.json](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots/fixed.cost-snapshot.json) (14 days, flat spend by resource, flat Transport Order volume) and [chatty-telemetry.cost-snapshot.json](../../src/HealthCheck/Norvia.HealthCheck.Infrastructure/Snapshots/chatty-telemetry.cost-snapshot.json) (the same volume, the toggle on from day 8: the Log Analytics workspace's daily spend about sixfold, every other resource where it was). They are **modelled, not measured** (`"measured": false`), and the sizes are a guess to replace. They are plain data: the snapshot adapter of the cost tools (#504) embeds them. A test ([AsFoundChattyTelemetryTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AsFoundChattyTelemetryTests.cs)) keeps the shape true: same volume, flat in the fixed one, the workspace rising and nothing else in the other.

## Faults

A **fault** is a runtime failure the presenter makes happen in one application, so a demo can show how the estate finds it ([GLOSSARY.md](../../GLOSSARY.md)). A toggle removes something that exists and stays until it is switched back; a fault breaks something for a few minutes and then stops. The Driver app's `driver-app-unavailable` refuses every Trip version with a 503 ([P11](../processes/tms/p11-trip-dispatch.md)), the Partner portal owns six that make a Carrier or Shipper misbehave ([below](#faults-of-the-partner-portal)) and one that disables a Logic App ([below](#the-fault-that-is-azure-state)), and the TMS and the Driver app own four that break Norvia's own side ([below](#faults-of-the-tms-and-the-driver-app)).

### The contract

One contract in [Norvia.ServiceDefaults](../../src/Norvia.ServiceDefaults/Faults), for every application:

| Part | What it is |
|---|---|
| **Name** and **owner** | `driver-app-unavailable`, owned by `driver-app`: the application it breaks. The application declares it with `builder.AddFaults(...)` and asks `IFaults.IsOn(name)` where it would fail |
| **Off by default** | Nothing is on after a start. The state is the application's own, held in its process (an in-app setting, so the same code runs locally and in Azure), so a restart switches every fault off |
| **Automatic expiry** | A fault is switched on for a duration and expires by itself. The default is 2 minutes and the longest allowed 5 (`Faults:DefaultDuration` and `Faults:MaxDuration` in the application's configuration); a longer duration is refused with 400. Switching on again starts the time over |
| **The guard** | `MapFaults()` maps `GET /v1/faults` (the faults, with whether each is on and how many seconds it has left) and `PUT /v1/faults/{name}` (`{ "on": true, "durationSeconds": 120 }`, or `{ "on": false }`). Every route requires the app role `Demo.Fault`: no token gets 401, a token without the role 403, an unknown fault 404. `MapFaults` is the only way to expose a fault |

The Driver app's old anonymous `GET` and `PUT /v1/settings/unavailable`, its `DriverAppSettings` table (dropped by a migration) and the `/settings` page of its screens are gone: nothing switches a fault without the role.

### The role `Demo.Fault`

An app role on each application that owns a fault (the Driver app API's and the TMS API's registrations), granted to the **Partner portal's managed identity**. The presenter panel calls the application as the portal: the portal is already the presenter's gate ([ADR 0024](../adr/0024-the-demo-uis-are-public-behind-an-entra-gate-for-the-presenter.md)), and its Easy Auth gets an ID token only, which no other application could accept. `identities.bicep` defines the role and the grant on each. Locally there is no Entra, so the AppHost signs the presenter in: the portal signs a token itself with the key every local application trusts, its client ID in `azp` and `Demo.Fault` in `roles` (`Faults__LocalTokens__*`). [Identity and access](../architecture/identity-and-access.md) lists the role.

### The presenter panel

`/presenter` in the Partner portal lists every fault of every application that owns one (`Faults:Owners` in the portal's configuration, set by the AppHost, plus the portal's own), with its owner, whether it is on and how long it has left, counting down. **Switch on** takes a duration (1, 2 or 5 minutes) and **Switch off** puts the fault back at once. The panel reads the faults again every ten seconds, so a fault that ran out, or was changed elsewhere, shows its true state.

### Cause and effect in the Integration console

A hop a fault made fail carries the fault's name: the Driver app's refusal of a Trip version is `Failed` with `DriverAppUnavailable` and `fault=driver-app-unavailable` (the TMS's refusals are in [their own table](#faults-of-the-tms-and-the-driver-app)), and the console's trail page shows it in the hop's **Fault** column, so the demo shows the cause beside the effect. It is a **tracked property**, a `fault` key of the [tracking contract](../coding-guidelines.md#tracking-contract), not a log line of its own: a log line would be a hop of its own and could not say which hop it belonged to, and a tracked key is found the way every other key is. Only the application the fault breaks sets it, on the hop it caused. The portal's switch logs itself with the same key (flow `fault-switch`), on a trail of its own.

### Faults of the Partner portal

The portal plays the outside parties, so their faults need no other system: each makes a Persona do what a real Carrier or Shipper can. All six are owned by `partner-portal`, declared with `AddFaults` (off by default, 2 minutes unless told otherwise, 5 at most) and listed in the presenter panel beside the Driver app's. The portal switches its own in process: the panel is the presenter's gate already, and a fault endpoint on the portal would need a token gate of its own. A restart switches them off.

| Fault | Process | What the Persona does | What the estate does | Where it shows |
|---|---|---|---|---|
| `carrier-tracking-unmapped-code` | P5 | A Carrier sends a Delivered event with the status code `DLV` instead of `POD` | APIM answers `202 Accepted` and queues it; the Carrier-tracking Logic App's map fails the run and dead-letters the message as `UnmappableTrackingMessage`; the Shipment stays In transit | The portal's `Send Tracking event` hop is `Succeeded` with `fault`; the Logic App's run fails |
| `carrier-answer-unmapped-reason` | P4 | A Carrier declines with the reason code `CAPACITY_EXCEEDED`, outside the list | The Carrier-answer Logic App's map fails the run (`CarrierAnswerUnreadable`, 400); the Carrier hears the answer is unreadable and the booking stays Requested | The portal's answer hop is `Refused` with `CarrierAnswerUnreadable` and `fault` |
| `carrier-booking-endpoint-down` | P4 | Every Carrier's booking endpoint answers 503 and keeps nothing | The booking Logic App's `Send_to_the_Carrier` fails (`CarrierUnavailable`) and reports the booking **Not delivered** | The endpoint's hop is `Failed` with `CarrierEndpointUnavailable` and `fault` |
| `carrier-proof-heic-photo` | P6 | A Carrier attaches `photo-of-delivery.heic` to its Proof of delivery (in the place of the fifth file when it has five) | APIM answers `202`; the TMS refuses the file by its content with `FileNotAccepted`; the message is dead-lettered and the blob kept | The portal's `Send Proof of delivery` hop is `Succeeded` with `fault` |
| `shipper-notification-endpoint-down` | P7 | Every Shipper's notification endpoint answers 503 and keeps nothing | `Send_to_the_Shipper` fails (`ShipperUnavailable`); the notification is **Not delivered** and `ShipperNotificationNotDelivered` is published | The endpoint's hop is `Failed` with `ShipperEndpointUnavailable` and `fault` |
| `carrier-cancellation-endpoint-down` | P7 | Every Carrier's cancellation endpoint answers 503 and keeps nothing | `Send_to_the_Carrier` fails (`CarrierUnavailable`); the Carrier booking is flagged **Carrier not told** and `CarrierCancellationNotDelivered` is published | The endpoint's hop is `Failed` with `CarrierEndpointUnavailable` and `fault` |

- **The message is what is faulty, not the Logic App.** The first four change what the Persona sends, at the wire (`CarrierTrackingFormat`, `CarrierAnswerFormat`, `CarrierProofOfDeliveryFormat`), so the portal still believes it succeeded and records what it sent. Only a Delivered event and a rejection are changed; a confirmation and any other event go as they are.
- **The portal's hop carries the fault, the Logic App's does not.** Only the application a fault breaks sets `fault`, on the hop it caused: for the first four that is the Persona's send, which `Succeeded` or was `Refused`. The failed run beside it is the effect, found by the same correlation ID on the Message trail.
- **Locally the same fault plays the same way.** The Persona is the same code, so the local stand-in and the workers (the tracking, Proof of delivery, booking, Shipper notification and Carrier cancellation workers) meet the same `DLV`, `.heic` file, reason code or 503 and settle it the same way: the tracking worker dead-letters `UnmappableTrackingMessage`, and the TMS refuses the file.
- **The two alert toggles complete the story.** With `flowsWithoutFailedRunAlert` and `entitiesWithoutDeadLetterAlert` listing `carrier-tracking`, `carrier-tracking-unmapped-code` re-enacts the headline incident: the Carrier posts "delivered", is told `202`, and nobody is told.

### Faults of the TMS and the Driver app

Norvia's own side breaks inside the application, so these are in-app settings that need no Azure write rights, the same code locally and in Azure. The TMS owns three, as `tms`; each is a **middleware gate** ([TmsFaultMiddleware](../../src/Tms/Norvia.Tms.Api/Faults/TmsFaultMiddleware.cs)) in the API it breaks, behind the tracking scope so the refused call is still on its Message trail. The Driver app's HEIC fault is in the Driver app API. All are off by default, 2 minutes unless told otherwise, 5 at most.

| Fault | Process | What breaks | What the estate does | Where it shows |
|---|---|---|---|---|
| `tms-unavailable` | P12 | Every call to the TMS API under `/v1` answers 503 and does nothing | The Driver reports Logic App retries (exponential, 3 retries) and the Trip's later reports wait behind it in the session; processed in order once the fault is off. A fault that outlasts the retries dead-letters the report as `TmsUnavailable` and the Driver sees it **Not recorded**: resubmit it. Other Logic Apps do as their rows in [failure-behaviour.md](../architecture/failure-behaviour.md) say | The TMS hop is `Failed` with `TmsUnavailable` and `fault` |
| `tms-tracking-event-api-failing` | P5 | `/v1/tracking-events` answers 503 | The Carrier-tracking Logic App abandons the message back to the queue, which redelivers it, and the run fails with `TmsUnavailable`; after expiry the next delivery records it. The queue's maximum delivery count (10) bounds how long it can wait, so a fault left at its maximum duration can still dead-letter a message as `MaxDeliveryCountExceeded` | The TMS hop is `Failed` with `TrackingEventApiUnavailable` and `fault`; the failed runs beside it |
| `tms-shipper-tracking-api-failing` | P8 | `/v1/shipper-tracking/*` answers 503 | API Management passes the failure on (`TmsUnavailable`, 503): the Shipper sees the error at once and nothing is queued, the contrast with P5's silent failure | The TMS hop is `Failed` with `ShipperTrackingApiUnavailable` and `fault`; APIM's own hop beside it |
| `driver-app-heic-photo` | P12 | A Proof of delivery the Driver app records stores its last photo (or one it adds when there is none) as `photo-N.heic`, `image/heic`, with the bytes of an HEIC file | The Driver reports Logic App sends it; the TMS refuses the file by its content with `FileNotAccepted` (400), the message is dead-lettered, the files stay in the transit container and the Driver sees the Proof of delivery **Refused** | The Driver app's `Deliver` hop is `Succeeded` with `fault`; the TMS refusal is on the same trail |

- **The gate never closes the way out.** `/v1/faults`, the health endpoints and the OpenAPI documents are not gated, so a fault can always be switched off and the platform still sees the TMS is up. When two gates apply, the more specific fault names the hop.
- **`driver-app-heic-photo` is the Driver app API's.** The state of a fault is held in the process that owns it, and the API is the process the presenter panel switches. The Deliver the API takes, such as the Simulator's Drivers' through the Driver action API, stores the HEIC photo; the Driver's own screens run in the Web process, which exposes no fault switch, and are not affected.
- **`driver-app-unavailable` is unchanged** and listed with the others under the contract ([above](#the-contract)).

### The fault that is Azure state

| Fault | Process | What breaks | What the estate does | Where it shows |
|---|---|---|---|---|
| `driver-reports-logic-app-disabled` | P12 | The Driver reports Logic App is disabled | Nothing reads the Drivers' reports: they stay **Sending** in the Driver app, the Planner's Trip never goes **In progress**, and Stale flags its Shipments once their next Tracking event is late. Nothing is lost: the reports wait in their sessions and, once the Logic App is enabled, are processed in order | The Logic App stops, so it has no runs of its own; the Driver app's reports and the Trip tell the story |

This is the one fault that is not application code ([#377](https://github.com/congruent-it/norvia-sandbox/issues/377)). It is owned by `partner-portal`, which switches it in process like its other faults, and then starts or stops the Logic App's site through Azure Resource Manager as its managed identity.

- **A narrow role, nothing more.** The identity holds the custom role `Norvia Logic App Switch` (read, start and stop a site) on that Logic App only. It can do nothing else to the estate ([the role](../architecture/identity-and-access.md#the-logic-app-switch-role)).
- **Expiry enables it again.** A fault's clock only says it is off, so a background task in the portal compares the fault with the Logic App every 5 seconds and enables the Logic App as soon as the fault is off, whether it expired or was switched off. The portal does the same at start, so a restart (which switches every fault off) never leaves the Logic App disabled. If Azure refuses to disable it, the fault is put back and the panel reports the error. If the portal is down at expiry, the Logic App stays off until it starts again.
- **Locally the worker stops by a flag.** The AppHost gives the portal and the Driver reports worker one flag file. While the fault is on the file exists, and the worker stops reading its subscription (`DriverReports:DisabledFlagPath`); when it ends the file is gone and the worker reads again.
- **Post-deploy smoke run.** Switch the fault on from `/presenter` for 2 minutes. Check that the Logic App is `Stopped` (`az webapp show --name $AZURE_ESTATE_DRIVER_REPORTS_LOGIC_APP --query state`), that a Driver's report in the Driver app stays Sending, and that the portal's identity cannot do anything else to the estate. Let it expire (or switch it off) and check that the Logic App is `Running` again and that the reports are Recorded in the order the Driver made them.

### Adding a fault

Declare it (`AddFaults` with a `FaultDefinition`), check `IFaults.IsOn` where the application should fail, put the name on the hop it breaks as `fault`, and expose the owner to the panel (`Faults__Owners__<owner>__Address` and `__Audience` on the portal, `Demo.Fault` on the owner's registration and for the portal's identity). A fault of the portal itself needs none of that: add it to `PartnerPortalFaultDefinitions` and `IPartnerPortalFaults`. `FaultEndpointGuardTests` fails when an endpoint is mapped by hand on a route that mentions a fault.

## Scenarios

A **scenario** is a named re-enactment: a [preset](#presets) plus the faults to switch on, a demo script and a post-deploy smoke run. Each preset is `fixed` plus only what the story needs, so the audience sees one cause. The smoke run applies the preset, stops and says what to do in the Partner portal and the presenter panel, asks what the presenter saw as yes or no, reads the estate, prints `PASS` or `FAIL` per check, and ends in the `fixed` state. The scenario of revenue that never invoices waits for E1 and E2 ([#10](https://github.com/congruent-it/norvia-sandbox/issues/10), [#11](https://github.com/congruent-it/norvia-sandbox/issues/11)) and is added then.

### Incident 1: the silent Carrier

| | |
|---|---|
| **Preset** | `silent-carrier`: no failed-run alert for `carrier-tracking`, no dead-letter alert for the tracking queue, and the Stale thresholds unset |
| **Fault** | `carrier-tracking-unmapped-code` (P5), switched on from `/presenter` |
| **Smoke run** | [smoke-test-as-found-silent-carrier.ps1](../../infra/smoke-test-as-found-silent-carrier.ps1) `<Transport Order number>` |

*Story.* A Carrier posts "delivered", is told `202 Accepted`, and believes it is done. Inside the integration layer the map fails the run and the tracking queue dead-letters the message as `UnmappableTrackingMessage`, and nobody is told: no alert, and Stale, which would flag a Shipment whose next Tracking event is late, has no thresholds. The Shipper's screen stays on In transit. Only a Consignee's phone call reveals it.

*What to switch on.* `infra/as-found.ps1 silent-carrier`, then the fault for 5 minutes, then the Carrier's Delivered event for a Shipment that is In transit.

*What the audience sees.* The Carrier is told `202`. The Shipper's tracking screen stays on In transit. No email arrives. In the Integration console the message trail of the Transport Order shows the Carrier's send as `Succeeded` with `fault`, the Carrier-tracking run `Failed`, and the dead-lettered message with its reason, which the presenter can resubmit once the cause is fixed.

*How to fix it.* `infra/as-found.ps1 fixed` is the fix and the **fixed run**: the failed-run and dead-letter alerts exist and go to the action group, whose email receiver is who is told, and the Stale thresholds are set. Run the fault again to show the email arrive within minutes; the dead-letter alert stays until the queue is emptied. The mapping itself is Norvia's to correct with the Carrier ([failure-behaviour.md](../architecture/failure-behaviour.md)).

### The look-alike

| | |
|---|---|
| **Preset** | `look-alike`: `shipperTrackingCached` |
| **Fault** | none |
| **Smoke run** | [smoke-test-as-found-look-alike.ps1](../../infra/smoke-test-as-found-look-alike.ps1) `<Transport Order number>` |

*Story.* The Shipper sees an old status, and it looks like incident 1. It is not: the TMS is right, and API Management answers the read from its cache.

*What to switch on.* `infra/as-found.ps1 look-alike`. As the Shipper read a Transport Order, as the Carrier send the next Tracking event, then read it again.

*What the audience sees.* The TMS and the Shipment's timeline show the new status, every run succeeded and the dead-letter queue is empty, but the Shipper still sees the old status for up to a day. Nothing is in the Integration console to find: the answer never reached the TMS, which API Management's own hop shows.

*How to fix it.* `infra/as-found.ps1 fixed` removes the cache from the Shipper tracking reads.

### The contrast

| | |
|---|---|
| **Preset** | `contrast`: the same as `fixed`. The estate is healthy and that is the point |
| **Faults** | `tms-shipper-tracking-api-failing` (P8) and `tms-tracking-event-api-failing` (P5), for 2 minutes |
| **Smoke run** | [smoke-test-as-found-contrast.ps1](../../infra/smoke-test-as-found-contrast.ps1) `<Transport Order number>` |

*Story.* The TMS fails for a few minutes, and the failure is loud and nothing is lost: the Shipper's read fails at once with the error, the Carrier's event is accepted and the Logic App retries it from the queue, and once the TMS is back the event is recorded, in order.

*What the audience sees.* The Shipper gets an error and not a stale answer. The console's TMS hops are `Failed` with the fault beside the failed runs, and after expiry the event is on the Shipment's timeline and the dead-letter queue holds nothing new. Show it after incident 1: the same cause (a failing hop) is loud when it is retried and silent when it is dead-lettered with no alert.

*How to fix it.* Nothing to fix: the faults expire (or are switched off in the panel).

## Presets

A preset is a file in [infra/presets](../../infra/presets) holding a value of `asFound`.

| Preset | State |
|---|---|
| `fixed` | Every alert, a receiver, every storage, secret and retention protection, every business safety net, no cache, the rate limit, and the report back to the Driver app. Nothing broken. What the estate is without a toggle |
| `as-found` | A realistic partial set. Alerts: six Logic Apps (the earlier-built Carrier flows, notifications and `billing`) and seven queues and subscriptions unalerted, the rest alerted. The incident flows are among the unalerted: `carrier-tracking`, `carrier-proof-of-delivery`, `billing`. Storage: no inbound lifecycle rule, files stay hot, no malware scanning; the transit rule is kept. A Carrier credential inlined, a short Application Insights retention, and no tracked properties on `billing`, `carrier-cancellation` and `shipper-notification`. Business: the Stale and Proof of delivery missing thresholds unset, the Late delivery check kept, the Shipper tracking cached and without a rate limit, the report back to the Driver app kept |
| `silent-carrier`, `look-alike`, `contrast` | The [scenarios'](#scenarios) presets: `fixed` plus only what the story needs (incident 1's missing alerts and Stale thresholds, the Shipper tracking cache, nothing) |

Every preset sets `chattyTelemetry` to `false`: the [spend toggle](#spend) is turned on by itself, not by a preset.

[estate.bicepparam](../../infra/estate.bicepparam) takes the object from the `ESTATE_ASFOUND` environment variable, as JSON, and the `fixed` preset when it is unset.

## The script

```sh
infra/as-found.ps1 <preset|toggle=value>
```

```sh
infra/as-found.ps1 as-found                              # a preset
infra/as-found.ps1 fixed
infra/as-found.ps1 noAlertReceiver=true                  # one toggle; the others stay as they are
infra/as-found.ps1 flowsWithoutFailedRunAlert=carrier-tracking,billing
infra/as-found.ps1 flowsWithoutFailedRunAlert=           # a list toggle, emptied
infra/as-found.ps1 noMalwareScanning=true
infra/as-found.ps1 logicAppsWithoutTrackedProperties=billing,carrier-cost
infra/as-found.ps1 staleThresholdsUnset=true
infra/as-found.ps1 shipperTrackingCached=true
```

It stores the toggles in the azd environment (`ESTATE_ASFOUND`), deploys the estate alone (`azd hooks run preprovision`, not the apps), deploys again the Logic Apps whose workflow a toggle changes (tracked properties, the report back to the Driver app), sets every application's log level for `chattyTelemetry`, sets the TMS API's settings for the business safety nets, and prints which toggles changed and the state now. Toggles are Azure-only: locally there are no alerts, lifecycle rules or scanning. For a live demo, switch to `as-found` beforehand, and run `infra/as-found.ps1 fixed` live as the fix.

## Reading a result

- Open **Monitor > Alerts > Alert rules** in the estate's resource group. A flow or queue with a toggle has no rule of its name; one without has exactly one, enabled.
- Open the action group `ag-norvia-…`: with `noAlertReceiver` it lists no email receiver, though the rules exist.
- Open the storage accounts: a toggle's account has no **Lifecycle management** rule (the inbound account's, the Driver app's transit account's, or the TMS's), and with `noMalwareScanning` the inbound account's **Microsoft Defender for Cloud** page has malware scanning off. The booking Logic App's **Environment variables** list `CARRIER_ENDPOINT_KEY` with `carrierSecretInlined`, and Application Insights' **Usage and estimated costs > Data retention** shows 30 days with `appInsightsRetentionShort`.
- Open the TMS API Container App's **Environment variables**: `ShipmentStale__ThresholdsSet`, `ProofsOfDelivery__MissingThresholdSet` and `LateDelivery__CheckEnabled` are `false` with their toggle. In API Management, the Shipper tracking operations' policies show `cache-lookup-value` and `rate-limit-by-key`. To see the cache, read a Transport Order as a Shipper, change its Shipment in the TMS, and read it again: the old status comes back for a day.
- Open any Container App's **Environment variables**: `Logging__LogLevel__Default` is `Information`, and `Debug` with `chattyTelemetry`. The cost shows in **Cost Management** a day or so later, as a rise of the Log Analytics workspace with no more Transport Orders.
- To see the incident, send a Tracking event the TMS refuses ([smoke-test-shipment-tracking.ps1](../../infra/smoke-test-shipment-tracking.ps1)): in `fixed` an email arrives within minutes; in `as-found` the message sits in the dead-letter queue until someone opens it in the Integration console.

## Testing

The fault contract has unit tests of its own ([Norvia.ServiceDefaults.UnitTests](../../tests/ServiceDefaults/Norvia.ServiceDefaults.UnitTests)): the expiry (a fault is off after its time, switching on again starts over), the default off, the duration limits, the role guard (401 without a token, 403 without `Demo.Fault`, on every route `MapFaults` maps) and the guard test that reads the applications' source and fails when a fault endpoint is mapped without `MapFaults`. The TMS's gates ([Norvia.Tms.Api.UnitTests](../../tests/Tms/Norvia.Tms.Api.UnitTests): off by default, the 503 and the `fault` key per gate, what is never gated, the expiry) and the Driver app's HEIC fault (the handler and the hop's key) have unit tests too. The Logic App fault's expiry (the Logic App is enabled once the fault is off, and after a restart) and the flag file are unit-tested in the Partner portal, the flag in the Driver reports worker, and the role's definition and assignment by a template test ([LogicAppSwitchRoleTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/LogicAppSwitchRoleTests.cs)). The Driver app's fault, the portal's client and the panel are covered by the Driver app's and the Partner portal's unit tests, which include each Persona's behaviour under its fault (the formats the client sends, the endpoints' 503 and their `fault` key, and the portal's own switches). The API's cases are in #414 and the panel's journeys in #415; the TMS and Driver app faults' are in #418 ([Norvia.Tms.Api.IntegrationTests.Faults](../../tests/Tms/Norvia.Tms.Api.IntegrationTests.Faults): the three TMS gates, the guard, the default off and the expiry, and the Driver reports and the HEIC Proof of delivery through the real workers; one test at a time, as a fault is on for the whole application) and #419. The local Carrier-tracking worker abandons a message the moment the TMS answers 503 and the queue delivers it at once, so its ten deliveries last about a tenth of a second; the tests set `Tracking:RedeliveryDelay` (none by default) so a message outlives a fault of seconds.

The business safety-net toggles have template and policy tests of their own ([AsFoundBusinessSafetyNetTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AsFoundBusinessSafetyNetTests.cs)): per policy toggle, `bicep snapshot` gives the four Shipper tracking policies and the toggle changes only its fragment (the rate limit removed, or the cache added to the three reads and never the file), the staged Driver reports workflow loses only its calls to the Driver app, and the presets set them as described. The TMS settings flow is covered by a unit test of the settings binding ([BusinessSafetyNetSettingsTests](../../tests/Tms/Norvia.Tms.Infrastructure.UnitTests/Settings/BusinessSafetyNetSettingsTests.cs)) and by the AppHost model tests, which check that each toggle sets exactly its own environment variable.

The storage, secret and retention toggles have template tests of their own ([AsFoundStorageTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AsFoundStorageTests.cs)): per toggle, the healthy template gives the setting and the toggle removes only it (the tracked properties are checked on the zip [stage-logic-app.ps1](../../infra/stage-logic-app.ps1) makes), and the `proofFilesStayHot` rule is checked in the AppHost model tests. The alert template tests ([AsFoundAlertsTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AsFoundAlertsTests.cs)) evaluate `alerts.bicep` with `bicep snapshot` for each preset and assert which rules exist: a listed entity has none, an unlisted one exactly one, and none is ever disabled. The drift guard above is the third test class. The spend toggle has its own: the AppHost model tests check that every application logs at `Information` and at `Debug` with `chattyTelemetry`, and [AsFoundChattyTelemetryTests](../../tests/Tms/Norvia.Tms.ArchitectureTests/AsFoundChattyTelemetryTests.cs) that every preset and the template's default have it off, that the script applies it, and that the cost snapshots keep their shape. After `azd up`, [smoke-test-as-found-alerts.ps1](../../infra/smoke-test-as-found-alerts.ps1) applies `as-found`, checks a sample of the rules is absent (tracking, booking and billing runs; the tracking, Proof of delivery and billing queues) and a sample is present (intake and Trip dispatch runs, the `driver-reports` subscription) with `az`, applies `fixed`, checks the whole sample exists and the action group has its receiver, then checks `noAlertReceiver`. It ends with one storage toggle and the Key Vault toggle: with `noInboundLifecycleRule` the inbound account has no lifecycle rule, and with `carrierSecretInlined` the booking Logic App has `CARRIER_ENDPOINT_KEY` (checked by name, the value never printed); under `fixed` neither. It then checks `staleThresholdsUnset` (the TMS API's setting is false, and only that one) and `shipperTrackingCached` (the Partner API's `get` Transport Order policy holds the cache; under `fixed` it does not). It ends with `chattyTelemetry`: every application's log level is `Debug` with it and `Information` under `fixed`.
