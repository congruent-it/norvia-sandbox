# Locally, a hosted worker plays the P7 Carrier cancellation Logic App

The P7 Carrier cancellation Logic App picks up `CarrierBookingCancelled` and `CarrierBookingWithdrawn` from its own subscription on the `shipment-events` topic, maps the event to the Carrier cancellation format, sends it to the Carrier Persona's cancellation endpoint in the Partner portal and reports the Carrier told or not told to the TMS (P7). Locally there is no Logic Apps runtime, so under Aspire a .NET worker, `Norvia.Integration.LocalCarrierCancellationWorker`, does the Logic App's work with the Logic App's own code, as ADR 0033 does for the Shipper notification:

1. It reads the same subscription the estate gives the Logic App, with the same resubmit-aware filter (`eventType = 'CarrierBookingCancelled' OR eventType = 'CarrierBookingWithdrawn'`), from the Service Bus emulator.
2. It maps the event with `CarrierCancellationMap`, the .NET library the Logic App's local function wraps (ADR 0019).
3. It calls the Carrier Persona's cancellation endpoint with the action's correlation ID and the credential the AppHost gives it for the run, standing in for the Key Vault secret `carrier-endpoint-key-<Carrier ID>`, the one the booking endpoint asks for.
4. It reports the booking as told (`report-cancellation-told`), or as not delivered (`report-cancellation-not-delivered`) with the status code (`0` for a timeout) and message of the technical cause, to the TMS as an identity holding `CarrierBookings.Report`, with the same correlation ID. If that report fails, the run fails and the booking stays without a Carrier told time, which the Planner's list shows (P7).
5. It retries a server error, a timeout, `408` and `429` with a bounded exponential policy, and any other client error not at all. When the retries are used up, or on a client error, it reports not delivered.

Like the workflow, which locks the message (peek-lock), the worker settles it itself: a message that can't be mapped is dead-lettered with the failure code `CarrierCancellationUnreadable` as `DeadLetterReason` and a readable message as `DeadLetterErrorDescription` (coding guidelines §7), and any other is completed once it is mapped, so a run that fails later is on the trail and is not redelivered. The tracking contract's keys (coding guidelines §7), with the Carrier booking number, go into its logging scope under the names the workflow's tracked properties use, so the Integration console finds a local run in the Aspire dashboard's telemetry (ADR 0027).

It is a project of its own for the reasons ADR 0030 and ADR 0033 give: the Logic App is triggered by a message, and a process of its own can be stopped to see a cancellation stay untold, as an unavailable Logic App would.

## Considered Options

- **Extending `Norvia.Integration.LocalBookingWorker` with a second subscription:** one project less, and the same Carrier endpoint credential. But the two flows share only the retry loop, and one process would hide a stopped cancellation flow behind a running booking flow.
- **No local flow:** a cancelled Carrier booking would never reach the Carrier's inbox locally, so there is no side-by-side demo and no end-to-end journey in CI.

## Consequences

- The worker is a second implementation of the workflow in `src/Integration/CarrierCancellationLogicApp/`. A change to the workflow's steps, its retry policy or its tracked properties must be made in both. CI checks the workflow's definition, and the worker's own unit tests check the same list: the map's output, the retry and no-retry statuses, the reports' content and the correlation ID on every call.
- The worker retries with its own bounded exponential policy, set to the workflow's numbers, so the number of attempts is the same in both places and a test can assert it.
- It writes no Logic App run history. Locally, the trail is in the Aspire dashboard's telemetry.
- It reads the AppHost's local credential from configuration and never from a file in the repository; in Azure the Logic App reads the Key Vault secret, and the secret never appears in the workflow definition.
- In Azure the Logic App has an identity of its own, holding `CarrierBookings.Report` beside the booking identity, and the role on each Carrier endpoint secret and nothing else in the vault.
- It runs only locally. `azd` never deploys it.
