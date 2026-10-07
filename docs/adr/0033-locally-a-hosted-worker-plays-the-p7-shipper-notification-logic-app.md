# Locally, a hosted worker plays the P7 Shipper notification Logic App

The P7 Shipper notification Logic App picks up `ShipperNotificationRequested` from its own subscription on the `shipment-events` topic, maps it to the Shipper notification format, sends it to the Shipper Persona's notification endpoint in the Partner portal and reports Sent or Not delivered to the TMS (P7). Locally there is no Logic Apps runtime, so under Aspire a .NET worker, `Norvia.Integration.LocalShipperNotificationWorker`, does the Logic App's work with the Logic App's own code, as ADR 0030 does for the booking:

1. It reads the same subscription the estate gives the Logic App, with the same resubmit-aware filter (`eventType = 'ShipperNotificationRequested'`), from the Service Bus emulator.
2. It maps the event with `ShipperNotificationMap`, the .NET library the Logic App's local function wraps (ADR 0019).
3. It calls the Shipper Persona's notification endpoint with the action's correlation ID and the credential the AppHost gives it for the run, standing in for the Key Vault secret `shipper-endpoint-key-<Shipper ID>`.
4. It reports the notification as sent, or as not delivered with the status code (none for a timeout) and message of the technical cause, to the TMS as the identity holding `ShipperNotifications.Report`, with the same correlation ID. If that report fails, the run fails and the notification stays Requested, which the Exception's list shows (P7).
5. It retries a server error, a timeout, `408` and `429` with a bounded exponential policy, and any other client error not at all. When the retries are used up, or on a client error, it reports not delivered.

Like the Logic App's Service Bus trigger, the worker completes a message once its run has started, so a run that fails is on the trail and is not redelivered. The tracking contract's keys (coding guidelines §7) go into its logging scope under the names the workflow's tracked properties use, so the Integration console finds a local run in the Aspire dashboard's telemetry (ADR 0027).

It is a project of its own for the reasons ADR 0030 gives for the booking worker: the Logic App is triggered by a message, and a process of its own can be stopped to see a notification stay Requested, as an unavailable Logic App would.

## Considered Options

- **Extending `Norvia.Integration.LocalBookingWorker` with a second subscription:** one project less. But the two flows share only the retry loop, and one process would then hide a stopped Shipper notification flow behind a running booking flow.
- **No local flow, the Partner portal's inbox filled by a test seed:** a Notify Shipper in the TMS would then never reach the Shipper's inbox locally, so there is no side-by-side demo and no end-to-end journey in CI.

## Consequences

- The worker is a second implementation of the workflow in `src/Integration/ShipperNotificationLogicApp/`. A change to the workflow's steps, its retry policy or its tracked properties must be made in both. CI checks the workflow's definition, and the worker's own unit tests check the same list: the map's output, the retry and no-retry statuses, the reports' content and the correlation ID on every call.
- The worker retries with its own bounded exponential policy, set to the workflow's numbers, so the number of attempts is the same in both places and a test can assert it.
- It writes no Logic App run history. Locally, the trail is in the Aspire dashboard's telemetry.
- It reads the AppHost's local credential from configuration and never from a file in the repository; in Azure the Logic App reads the Key Vault secret, and the secret never appears in the workflow definition.
- It runs only locally. `azd` never deploys it.
