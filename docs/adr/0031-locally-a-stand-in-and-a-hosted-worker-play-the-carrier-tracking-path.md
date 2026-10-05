# Locally, a stand-in and a hosted worker play the Carrier-tracking path

A Carrier's Tracking event is accepted at API Management, put on a Service Bus queue and recorded with the TMS later by the Carrier-tracking Logic App (ADR 0008). Locally there is neither API Management nor a Logic Apps runtime, so two existing local pieces split the path as it splits in Azure:

1. **`Norvia.Integration.LocalPartnerApi` plays the API Management operation** (ADR 0023). `POST /partner/v1/carrier-tracking` makes the same checks in the same order as `submit-carrier-tracking.policy.xml` (subscription key, token, role `Tracking.Submit`, a Carrier for `azp`), creates the correlation ID, puts the message on the `carrier-tracking` queue and answers `202 Accepted`. The message carries the application properties `correlationId`, `partyId` (the Carrier ID from the token), `sourceMessageId` (the Carrier's event ID, when it has one) and `flow` = `carrier-tracking`. It calls neither the TMS nor a Logic App. When the queue can't be reached it answers `503`, as the policy does, so the Carrier sends the event again.
2. **`Norvia.Integration.LocalTrackingWorker` plays the Carrier-tracking Logic App** (ADR 0030's pattern). It reads the queue in peek-lock mode, maps the message with `CarrierTrackingMap`, records the event with the TMS as the identity holding `TrackingEvents.Record` under the message's correlation ID, and settles the message by what the TMS answered, through `TmsTrackingOutcome`, which the workflow's expressions are tested against: a recorded event is completed; a client error is dead-lettered with `DeadLetterReason` = the problem's failure code and `DeadLetterErrorDescription` = its message, and not retried; a server error, throttling or timeout is abandoned, so the queue delivers it again up to its maximum delivery count and then dead-letters it itself. A status code the map doesn't know dead-letters the message as `UnmappableTrackingMessage` and fails the run.

The worker is a project of its own for the reason ADR 0030 gives: the Logic App is triggered by a message, not by an HTTP call, and running it apart from the Partner API stand-in lets a developer stop it to see messages wait on the queue, which is the presenter-panel fault of P5. Unlike the booking trigger it settles the message itself, because the workflow's retry comes from the queue.

The tracking contract's keys (coding guidelines §7) go into the worker's and the stand-in's logging scopes under the names the policy's trace metadata and the workflow's tracked properties use, so the Integration console finds a local trail in the Aspire dashboard's telemetry (ADR 0027).

## Considered Options

- **A queue consumer inside `LocalPartnerApi`:** one project less, but the consumer would share the lifetime of the intake request handler, and the fault in which the Logic App is down while API Management still accepts could not be shown locally.
- **Extend `LocalBookingWorker` to read the tracking queue:** it is named for, configured for and tested as the booking flow, and holds the `CarrierBookings.Report` identity, not the record role.
- **The stand-in calling the TMS directly and answering 200:** the Carrier would see failures it never sees in Azure, and ADR 0008's asynchronous behaviour could not be reproduced.

## Consequences

- The stand-in is a second implementation of the policy and the worker a second implementation of the workflow. A change to the policy's checks, headers or application properties, or to the workflow's settling rules or tracked properties, must be made in both. `SubmitCarrierTrackingPolicyTests` and `TrackingWorkflowTests` read the Azure side, and the stand-in's and the worker's unit tests check the same list.
- The workflow's Service Bus operation IDs (`peekLockQueueMessages`, `completeQueueMessage`, `abandonQueueMessage`, `deadLetterQueueMessage`) can't be run without a Logic Apps runtime. The unit tests read the definition; only a deployed run proves them.
- The tracking contract's keys, the local token options and the local TMS token are one shared project, `Norvia.Integration.LocalSupport`, used by the Partner API stand-in and both workers. The worker settles by `CarrierTrackingMap.Map`'s result, not by catching the exception the workflow's local function throws.
- Neither runs in Azure: `azd` never deploys them.
