# Locally, a hosted worker plays the P4 booking Logic App

The P4 booking Logic App picks up `ShipmentReleased` from its own subscription on the `shipment-events` topic, maps it to the Carrier booking format, sends it to the Carrier Persona's booking endpoint in the Partner portal and reports the outcome to the TMS (P4). Locally there is no Logic Apps runtime, so under Aspire a .NET worker, `Norvia.Integration.LocalBookingWorker`, does the Logic App's work with the Logic App's own code, as ADR 0023 does for intake:

1. It reads the same subscription the estate gives the Logic App, with the same resubmit-aware filter, from the Service Bus emulator.
2. It maps the event with `CarrierBookingMap`, the .NET library the Logic App's local function wraps (ADR 0019).
3. It calls the Persona's booking endpoint with the release's correlation ID and the credential the AppHost gives it for the run, standing in for the Key Vault secret.
4. It reports the booking as sent, or as not delivered with the status code and message of the technical cause, to the TMS as the identity holding `CarrierBookings.Report`, with the same correlation ID. If that report fails, the run fails and the booking stays Requested until it is overdue (P4).
5. It retries a server error or timeout with a bounded exponential policy, and a client error not at all. When the retries are used up, or on a client error, it reports not delivered.

Like the Logic App's Service Bus trigger, the worker completes a message once its run has started, so a run that fails is on the trail and is not redelivered.

The tracking contract's keys (coding guidelines §7) go into the worker's logging scope under the names the workflow's tracked properties use, so a local run reads as one in Azure, and the Integration console finds it in the Aspire dashboard's telemetry (ADR 0027).

The worker is a project of its own because the Logic App is triggered by a message, not by an HTTP call. The intake stand-in is a web app that answers the Partner portal's request, and a Service Bus consumer inside it would have no request to belong to. Running the booking flow in its own process also keeps it independent of the Partner API stand-in: it can be stopped to see a booking stay "not yet sent", as an unavailable Logic App would.

## Considered Options

- **A background service inside `Norvia.Integration.LocalPartnerApi`:** one project less. But that project is named for the Partner API and its health, its configuration and its tests are about the intake request, and the booking flow would share its lifetime.
- **No local booking flow, the Partner portal's inbox filled by a test seed:** nothing to build. But a release in the TMS UI would then never reach the Carrier's inbox locally, so there is no side-by-side demo of booking and no end-to-end journey in CI.
- **The Logic Apps runtime in a container:** closest to Azure, but it needs an Azure connection for its managed connectors and the same start-up cost ADR 0023 rejected.

## Consequences

- The worker is a second implementation of the workflow in `src/Integration/BookingLogicApp/`. A change to the workflow's steps, its retry policy or its tracked properties must be made in both. CI checks the workflow's definition, and the worker's own unit tests check the same list: the map's output, the retry and no-retry statuses, the reports' content and the correlation ID on every call.
- The worker retries with its own bounded exponential policy, set to the workflow's numbers, rather than the standard resilience handler, so the number of attempts is the same in both places and a test can assert it.
- It writes no Logic App run history. Locally, the trail is in the Aspire dashboard's telemetry.
- It reads the AppHost's local credential from configuration and never from a file in the repository; in Azure the Logic App reads the Key Vault secret, and the secret never appears in the workflow definition.
- It runs only locally. `azd` never deploys it.
