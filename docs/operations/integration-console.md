# Integration console

The Integration console is where the Integration operator finds out what the integration layer did with a message, and resolves the messages that failed in it. It shows Message trails, failed Logic App runs and dead-lettered messages, and it can resubmit or discard a dead-lettered message. It reads what the estate already records and keeps nothing of its own ([ADR 0017](../adr/0017-the-integration-console-reads-the-estates-telemetry-and-stores-nothing.md)). Terms are defined in [CONTEXT.md](../../CONTEXT.md).

| | |
|---|---|
| **Used by** | The Integration operator. In demos the presenter plays this role, as it plays the Planner in the TMS |
| **Shows** | Message trails found by correlation ID or business key, failed Logic App runs, dead-lettered messages per queue and subscription |
| **Changes** | Only dead-letter queues: resubmitting or discarding a dead-lettered message |
| **Owns** | Nothing. It has no database |
| **Reads from** | Application Insights (the search index; locally the Aspire dashboard's telemetry), the Logic Apps management API (run history and step bodies), Service Bus (dead-letter queues) |

## Scope

The console is built in two slices:

- **Slice 1, after [P1](../processes/tms/p1-transport-order-intake.md):** sign-in, search, the Message trail and run details. It's read-only.
- **Slice 2, after [P5](../processes/tms/p5-shipment-tracking.md):** the failed runs and dead-letter queue panels, resubmit, discard and the audit of both. P5 adds the first queue with a dead-letter queue.

No business process depends on the console. It stays off every business path, so a slice that slips blocks nothing.

The console only works well if every flow follows the [tracking contract](../coding-guidelines.md#tracking-contract). That contract applies from P1 onwards, whether or not the console exists yet.

## Message trail

A Message trail is everything the integration layer did under one correlation ID. API Management creates the correlation ID at the edge for a partner's call. An application creates one when a Norvia employee's action publishes an event, for example a Planner releasing a Shipment. From there, the trail includes every queue, subscription, Logic App run and application call that followed, fan-out included. One `TransportOrderAccepted` event can start runs in several subscriptions, and all of them belong to the same trail.

Trails don't chain. When the Planner later plans the Transport Order that P1 accepted, the release starts a new trail. Business keys join them: searching for a Transport Order number returns every trail that carried it.

### Where a trail comes from

| Hop | Source | Delay |
|---|---|---|
| API Management request and response | Application Insights (APIM diagnostics) | Ingestion delay, usually 1 to 5 minutes |
| Logic App run, with its tracked properties and outcome | Application Insights (Logic Apps Standard telemetry) | Ingestion delay |
| Application call and published event | Application Insights (OpenTelemetry from `ServiceDefaults`) | Ingestion delay |
| Run steps and their input and output bodies | Logic Apps management API, fetched when opened | Live |
| Dead-lettered message and its body | Service Bus dead-letter queue, peeked when opened | Live |

Search and the trail's outline come from Application Insights, so they can be a few minutes behind. The trail says so at the top ("telemetry may be a few minutes behind"). The failed runs and dead-letter queue panels read their sources directly, so a failure shows up there within seconds. In a demo, the panels are where the failure appears, and the trail explains it afterwards.

## Screens

### Search

- One search box that accepts a correlation ID, a Transport Order number, a Shipment number, a Carrier booking number, a Trip number or a source message ID (a Carrier's event ID or document ID, or a Driver app event ID).
- A time range, the last 24 hours by default and at most the retention period (see [Retention](#retention)).
- Results show one row per trail: correlation ID, the flows it went through, first and last seen, its worst outcome, and the business keys it carried. Trails with a failure come first: a trail has a failure when a hop failed or was dead-lettered. A refusal is the answer the caller was meant to get, and a retry may still succeed, so neither counts.
- Search results never show message bodies (see [Personal data](#personal-data)).

### Message trail

- The trail's hops in time order, indented where it fans out, each with its flow, time, duration and outcome (`Succeeded`, `Refused`, `Retrying`, `DeadLettered`, `Failed`) and failure code.
- Each hop is named after its application and what happened there: an application call by its method and the path actually called, such as `tms-api: POST /v1/transport-orders/TO-0000042/assign`, never its route template; a published event after the event, such as `tms-api: publish ShipmentPlanned`; what an application did outside any call after the span it started for it, such as `partner-portal-web: Send booking` or `tms-api: Rate Transport Order`, not a framework's span; and a Logic App run after its workflow.
- The business keys the trail carried, each a link that searches for it.
- A Logic App run opens its [run detail](#run-detail). A dead-lettered hop links to the message in its [dead-letter queue](#dead-letter-queues) while it's still there, or says whether it was resubmitted or discarded, and by whom (from the [audit](#audit)).
- A resubmitted message continues the same trail, because it keeps its correlation ID. The trail shows the failed attempt and the later one.

### Run detail

- A run's steps with their status, duration and error, fetched live from the Logic Apps management API.
- Opening a step shows its input and output bodies. Steps that use secure inputs or outputs show that they're hidden.
- Once run history has passed its retention, the run detail says "body no longer kept (run history is kept for 90 days)". The hop stays on the trail, because Application Insights has its own retention.
- A run is never resubmitted from here (see [Resubmit and discard](#resubmit-and-discard)).

### Failed runs (slice 2)

- The failed runs of every Logic App in the estate over the last 24 hours, newest first, filterable by flow.
- Each row shows the flow, the time, the failing step and its error, and the correlation ID, which opens the trail.
- The panel refreshes itself every few seconds while it's open.

### Dead-letter queues (slice 2)

- One row per queue and subscription in the estate, with its dead-letter count and the age of its oldest message. Queues with dead-lettered messages come first.
- Opening one lists its messages, peeked and not received: enqueued time, dead-letter reason and description, delivery count, correlation ID and business keys. Opening a message shows its body and application properties.
- Messages can be selected, one or several, to resubmit or discard.
- The panel refreshes itself every few seconds while it's open.

## Resubmit and discard

The console only acts on dead-lettered messages. It never resubmits a Logic App run.

| What failed | What the console offers | Why |
|---|---|---|
| A message on a queue's dead-letter queue | Resubmit to that queue, or discard | The queue has one consumer, so a resubmit reaches exactly it |
| A message on a subscription's dead-letter queue | Resubmit to that subscription only, or discard | Resending to the topic would start every other subscriber again. The `resubmitTo` rule below targets one subscription |
| A failed run of a request-response flow (P1 intake, the P4 Carrier answer) | View only | The partner has already been told the call failed. A resubmit would create, for example, a Transport Order the Shipper believes was not received |
| A failed run whose message went to a dead-letter queue | Resubmit the message, not the run | The message is the durable record. The run's lock on it is gone |

Rules:

- **Resubmitting** sends a copy of the message with the same body, the same application properties, the same `SessionId`, `Subject` and `ContentType`, and a new message ID. Keeping the `SessionId` puts a Driver's report back in its Trip's session (P12). It adds `resubmittedFrom` (the original message ID) and, for a subscription, `resubmitTo`. When the send succeeds, the original is completed on the dead-letter queue. A copy that fails again is dead-lettered again, as any message would be.
- **Resubmit to one subscription:** Service Bus can't send to a single subscription, so the console sends to the topic with `resubmitTo` set to the subscription's name. Every subscription's filter follows the [tracking contract](../coding-guidelines.md#tracking-contract): it takes a message that carries its own name in `resubmitTo`, and it ignores one that carries another name.
- **Resubmitting is safe to repeat.** Every command is idempotent (coding guidelines §6), so a message that already partly took effect is recorded once. P9 relies on this for billing.
- **Discarding** requires a reason, and completes the message on the dead-letter queue.
- **Several at once:** a selection on one queue or subscription is resubmitted or discarded in one action, for example after the ERP was down for ten minutes (P9). A selection is resubmitted in its original enqueue order, so a Trip's dead-lettered Start Trip goes back before its Stop reports (P12). Each message is audited on its own.
- **Bodies are never edited.** A mapping fault is fixed in the map and deployed, and then the original message is resubmitted. Editing would bypass the validation the flow exists to do.

### Audit

Every resubmit and discard writes an audit event to Application Insights: who (from the token), which action, which message (entity, message ID, correlation ID, business keys), when, and the reason for a discard. The audit event carries the correlation ID, so it appears on the Message trail. It's kept as long as Application Insights keeps everything else.

## Personal data

Message bodies hold personal data such as Consignee names and addresses.

- Search results, trails and panels show only the tracking contract's keys, never bodies.
- A body is fetched only when someone with `IntegrationConsole.Read` opens a step or a message, and it's shown in full. Masking would need a map per message type, and a mapping fault is often in exactly those fields.
- The console doesn't copy bodies anywhere, including its own logs.
- Logic Apps use secure inputs and outputs only for steps that handle secrets, such as the Carrier endpoint credentials in P4. Anywhere else, they would hide the body the Integration operator needs.

## Retention

The console shows what the estate still remembers. One estate Bicep parameter, `integrationRetentionDays` (90 by default), sets:

- Application Insights and Log Analytics table retention: the search index and the audit;
- Logic Apps Standard run history (`Workflows.RuntimeConfiguration.RetentionInDays`): run steps and bodies;
- the lifecycle rules of the integration layer's transit containers: P6's inbound Proof of delivery container and P12's Driver app transit container.

Messages on a dead-letter queue stay until someone resubmits or discards them. Message time-to-live doesn't apply there.

Ninety days is the retention Application Insights tables get at no extra cost, and it's the Logic Apps Standard default. A failure the size of the headline incident (four days) needs far less. Norvia's lasting record of what happened is the TMS and the ERP, not the integration layer.

## Access and identity

- Integration operators sign in with Entra ID, through Container Apps' built-in authentication in front of the console ([ADR 0026](../adr/0026-the-integration-console-signs-operators-in-with-easy-auth-and-checks-their-app-role.md)). Entra signs in only users holding one of the console's roles, and every page requires `IntegrationConsole.Read`. The console's app registration has two app roles:
  - `IntegrationConsole.Read`: search, trails, run details, panels and bodies;
  - `IntegrationConsole.Resubmit`: also resubmit and discard.
- The console checks the user's role, then calls Azure with its own managed identity, never with the user's token. The estate Bicep grants that identity:
  - Log Analytics Reader on the workspace behind Application Insights;
  - read access to run history on each Logic App Standard app;
  - Reader on the Service Bus namespace, for dead-letter counts;
  - Azure Service Bus Data Receiver and Data Sender on the namespace, to peek, complete and resend dead-lettered messages.
- The presenter's account holds both roles.

## Architecture

The console follows the coding guidelines with one deviation, stated here as §1 requires: **it has no Domain project, no repositories or specifications, and no EF Core**. It has no aggregates and no database, so those layers would be empty.

| Project | Contains |
|---|---|
| `IntegrationConsole.Application` | Use cases (search, open trail, open run, list dead-letter queues, resubmit, discard), DTOs, and the ports `ITrailSource`, `IWorkflowRuns`, `IDeadLetterQueues` and `IAuditLog` |
| `IntegrationConsole.Infrastructure` | Adapters over the Azure SDKs: Application Insights queries, the Logic Apps management API, Service Bus; locally, the Aspire dashboard's telemetry API |
| `IntegrationConsole.Web` | Blazor web app, Interactive Server for the self-refreshing panels |

The dependency rule, the architecture tests, the mediator, the 80% line coverage, bUnit and Playwright all still apply. It runs under Aspire and deploys to Container Apps like the other applications (ADR 0004). Locally it searches the telemetry the Aspire dashboard holds, since no local app sends telemetry to Application Insights ([ADR 0027](../adr/0027-locally-the-integration-console-searches-the-aspire-dashboards-telemetry.md)); given a deployed estate's workspace, it reads that estate with the developer's credentials instead. Logic Apps exist only in Azure. It can also list the Service Bus emulator's dead-letter queues.

## Testing

- **Unit tests** on the Application layer, with the four ports replaced by NSubstitute and time by `TimeProvider`. For example, a subscription message is resubmitted with `resubmitTo`, a discard without a reason is refused, and a trail with a dead-lettered hop comes first in search.
- **Integration test of the Service Bus adapter against the emulator**, under Aspire's test host:
  - a dead-lettered queue message is resubmitted, and arrives on its queue with its properties and `resubmittedFrom`;
  - a dead-lettered subscription message is resubmitted, and reaches only its own subscription, not the topic's other subscriptions;
  - on a session-enabled subscription, a selection is resubmitted in its original enqueue order, and each copy keeps its `SessionId`;
  - a discarded message is gone from the dead-letter queue;
  - counts and the oldest message's age are right.
- **The Application Insights and Logic Apps adapters** are thin wrappers over their SDKs and are covered by the smoke run.
- **The Aspire dashboard adapter** is unit tested against the dashboard's OTLP JSON, faked at the HTTP boundary: it finds a trail by a business key or its correlation ID, within the range, from the tracking keys only.
- **bUnit and Playwright** against the Web project with faked ports. For example, a user with only `IntegrationConsole.Read` sees no resubmit or discard, and "body no longer kept" shows for an expired run.
- **Post-deploy smoke run, slice 1:** book as Brouwer Dranken. Find the trail by the Transport Order number and by the correlation ID. It shows the APIM call, the intake run and the TMS call, and the run's steps open with their bodies. See [Presenter smoke run](#presenter-smoke-run).
- **Post-deploy smoke run, slice 2:**
  1. Stop the TMS container app, and post a `POD` as Van Dijk Transport.
  2. After its maximum delivery count, the message reaches the tracking queue's dead-letter queue, and the dead-letter queue panel shows it within seconds.
  3. Start the TMS and resubmit the message. The Shipment moves to Delivered, and the trail shows the failed attempts and the resubmit.
  4. Post a Tracking event for an unknown reference. Discard it with a reason, and find the audit event on its trail.

## Presenter smoke run

After a deployment, and not in CI, the presenter shows the audience the path a booking took. Do it after the demo smoke run of the Norvia Partner API ([infra/smoke-test-partner-api.sh](../../infra/smoke-test-partner-api.sh)), signed in as the presenter the estate was provisioned for. The postdeploy hook prints each UI's address.

1. **Book.** In the Partner portal, choose Brouwer Dranken in the Persona switcher and send a booking. Note the Transport Order number Norvia answers with, and Norvia's reference for the attempt under it: that is the correlation ID API Management created.
2. **Check the sources.** Run `infra/smoke-test-integration-console.sh <Transport Order number> <correlation ID>`. It waits for Application Insights, which is usually 1 to 5 minutes behind, and checks what the console reads. The trail is found by the Transport Order number and by the correlation ID the portal showed, and it's one successful `transport-order-intake` trail. The trail holds the API Management call, the intake Logic App run (for Brouwer Dranken, `SHP-0001`) and the TMS call, all under that correlation ID. The run's steps open, and the map's and the TMS call's input and output bodies can be fetched. It prints `PASS` or `FAIL` per check, never a body, and ends with the console's pages for this booking. The correlation ID is optional, and without a number it takes Brouwer Dranken's latest booking of the last hour. It reads as you, not as the console's identity, so the console's own access shows only in the steps below.
3. **Search.** In the Integration console, search for the Transport Order number: one trail, with the flow `transport-order-intake`, outcome `Succeeded` and the Transport Order number among its business keys. Search again for the correlation ID: the same trail.
4. **Follow the trail.** Open it. In time order it shows the API Management call, the intake run and the TMS call the run made, with the correlation ID at the top.
5. **Open the run.** On the intake run's hop, choose *Open run*. Its steps show with status and duration. Open `Map_the_booking` for the booking as Brouwer Dranken sent it and the Transport Order it became, and `Submit_to_the_TMS` for the call to the TMS and its answer.

If a hop is still missing after the script's 10 minutes, and the run is younger than its retention, the flow that should have recorded it is breaking the [tracking contract](../coding-guidelines.md#tracking-contract). Fix that flow, or file it against its process; don't work around it in the console.

## Deferred

These were left out on purpose while designing the console, each with its reason.

- **Alerts.** They stay in Azure Monitor, where the as-found toggles switch them off. The console doesn't raise alerts, though its panels make a missing one obvious.
- **Costs and the Azure bill.** The scenario's second incident is about cost, not messages, and needs its own view.
- **A panel for failures that never reach a Logic App**, such as API Management's 401, 403 and 429. They're findable in search, but there's no panel for them.
- **A long-term message archive.** It would put Consignee personal data in a second store. Norvia's lasting record is the TMS and the ERP.
- **Editing a message before resubmitting.** A fault is fixed in the map, not in the message.
- **Resubmitting a Logic App run.** See [Resubmit and discard](#resubmit-and-discard).
- **As-found toggles.** They are added after the fixed state is built, as for P1 to P12. The candidates:
  - Toggle: Logic Apps without tracked properties, so a trail can be found by correlation ID but not by Shipment number.
  - Toggle: Application Insights retention shorter than run history, so run bodies outlive the index that finds them.
  - Together with P5's presenter-panel fault (an unmappable Carrier code) and missing alerts, the console re-enacts the headline incident's discovery: the message was visible on the dead-letter queue the whole time, and no one was looking.
