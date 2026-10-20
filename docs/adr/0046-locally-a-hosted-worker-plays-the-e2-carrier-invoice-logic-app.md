# Locally, a hosted worker plays the E2 Carrier invoice Logic App

The E2 Carrier invoice Logic App sits behind an HTTP trigger only API Management may call (ADR 0020). It maps a Carrier's invoice into the Canonical Model, receives it at the ERP billing API and answers the Carrier at once with each line's result. Locally there is no API Management and no Logic Apps runtime, so under Aspire the stand-in Partner API plays API Management (ADR 0023) and a .NET worker, `Norvia.Integration.LocalCarrierInvoiceWorker`, plays the Logic App behind its trigger, as ADR 0043 does for the E1 Logic Apps. The Partner portal's draft invoice screen then submits an invoice locally as it does in Azure:

1. `LocalPartnerApi` takes `POST /partner/v1/carrier-invoices` and makes the policy's checks in its order: subscription key, token, the role `CarrierInvoices.Submit` (a Shipper Persona gets `403`) and the Carrier for the token's `azp`. It creates the correlation ID and traces the refusals and the answer with the tracking contract's keys under flow `carrier-invoice`.
2. It posts the invoice, as the Carrier sent it, to the worker's trigger with `X-Carrier-ID` and `X-Correlation-ID` from the edge alone and a token of API Management's local client. The worker refuses any other caller with `403`, as the workflow's first step does.
3. The worker maps the invoice with `CarrierInvoiceMap` and posts the submission to `POST /v1/carrier-invoices` with the correlation ID and a token of the one identity holding `CarrierInvoices.Receive`, signed with the AppHost's local key (`LocalTokens.CarrierInvoiceLogicAppClientId`).
4. It retries a server error, a timeout, `408` and `429` with the workflow's bounded exponential policy (3 retries, 5 s up to 20 s) and any other client error not at all.
5. It relays the ERP's answer with `CarrierInvoiceResponses`, and the stand-in returns it to the Carrier unchanged. A worker the stand-in can't reach is answered `502` `NOT_RECEIVED`.

The tracking contract's keys go into the logging scope under the names the workflow's tracked properties use, so the Integration console finds a local run in the Aspire dashboard's telemetry (ADR 0027).

## Considered Options

- **Doing the Logic App's work inside the stand-in, as the intake and Carrier-answer Logic Apps are:** it would leave no place for the workflow's own identity, retries and tracking, and the Carrier invoice path is the one where the ERP call can run for a minute with retries. A separate process keeps the two roles as they are in Azure.
- **A Service Bus queue between them:** the Carrier is answered synchronously (P4), so a queue would only add a reply path.

## Consequences

- The worker is a second implementation of the workflow in `src/Integration/CarrierInvoiceLogicApp/`. A change to the workflow's steps, retry policy or failure codes must be made in both. CI checks the workflow's definition, and the worker's unit tests check the same list: the submission as the map makes it, the retry and no-retry statuses, the correlation ID and role on the call, and the relay's answers.
- The stand-in's call to the worker waits as long as the workflow's retries can run and does not retry itself, as API Management does not.
- It writes no Logic App run history. Locally, the trail is in the Aspire dashboard's telemetry.
- It runs only locally. `azd` never deploys it.
