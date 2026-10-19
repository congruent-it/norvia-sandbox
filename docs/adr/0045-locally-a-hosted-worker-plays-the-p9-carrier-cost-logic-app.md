# Locally, a hosted worker plays the P9 Carrier cost Logic App

The P9 Carrier cost Logic App picks up `CarrierCostIncurred` from its own `carrier-cost` subscription on the `shipment-events` topic and records the Carrier cost with the ERP billing API (E2). Locally there is no Logic Apps runtime, so under Aspire a .NET worker, `Norvia.Integration.LocalCarrierCostWorker`, does the Logic App's work, as ADR 0043 does for the Billing Logic App. A Carrier Shipment delivered in the TMS then reaches the ERP's Open Carrier costs locally as it does in Azure:

1. It reads the same subscription the estate gives the Logic App, with the same resubmit-aware filter (`eventType = 'CarrierCostIncurred'`), from the Service Bus emulator. Only Carrier Shipments have a Carrier cost, so the event type is the whole condition.
2. It posts the message body to `POST /v1/carrier-costs` as it is, because the ERP accepts the Canonical Model and there is nothing to map, with the event's correlation ID and a token of the one identity holding `CarrierCosts.Record`, signed with the AppHost's local key (`LocalTokens.CarrierCostLogicAppClientId`).
3. It retries a server error, a timeout, `408` and `429` with a bounded exponential policy set to the workflow's numbers (3 retries, 5 s up to 20 s), and any other client error and any conflict not at all.
4. It completes the message when the ERP answers `2xx`, a repeat included. Otherwise it dead-letters it with the ERP's code as `DeadLetterReason` (`ErpUnavailable` when the retries are used up, `ErpCallRefused` when a refusal carries no code) and its message as `DeadLetterErrorDescription`. A message that isn't a `CarrierCostIncurred` is dead-lettered as `CarrierCostUnreadable` (coding guidelines §7).

The tracking contract's keys go into the logging scope under the names the workflow's tracked properties use, so the Integration console finds a local run in the Aspire dashboard's telemetry (ADR 0027).

## Considered Options

- **No local flow:** the ERP would show Carrier costs locally only when a developer called its API by hand, and there would be no end-to-end journey from a delivery to the Open Carrier costs list.
- **Having the TMS or the ERP call each other:** it would break the rule that applications meet only through the integration layer (ADR 0001, ADR 0003).

## Consequences

- The worker is a second implementation of the workflow in `src/Integration/CarrierCostLogicApp/`. A change to the workflow's steps, retry policy or failure codes must be made in both. CI checks the workflow's definition, and the worker's unit tests check the same list: the body sent as it is, the retry and no-retry statuses, the correlation ID and role on the call, and the dead-letter reasons.
- It writes no Logic App run history. Locally, the trail is in the Aspire dashboard's telemetry.
- It runs only locally. `azd` never deploys it.
