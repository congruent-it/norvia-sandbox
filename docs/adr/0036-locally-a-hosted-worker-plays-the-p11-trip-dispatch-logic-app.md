# Locally, a hosted worker plays the P11 Trip dispatch Logic App

The P11 Trip dispatch Logic App picks up `TripDispatched` from its own `trip-dispatch` subscription on the `trip-events` topic, sends the Trip to the Driver app's Trip endpoint and reports the version delivered or not delivered to the TMS (P11). Locally there is no Logic Apps runtime, so under Aspire a .NET worker, `Norvia.Integration.LocalTripDispatchWorker`, does the Logic App's work, as ADR 0033 and ADR 0034 do for the P7 Logic Apps:

1. It reads the same subscription the estate gives the Logic App, with the same resubmit-aware filter (`eventType = 'TripDispatched'`).
2. It sends the Trip as the event carries it: there is no map and so no local function, since the Driver app's Trip endpoint takes the canonical Trip (ADR 0015).
3. It calls the endpoint with the correlation ID and the credential the AppHost generates for the run, standing in for the Key Vault secret `driver-app-endpoint-key`; the Driver app is given the same one.
4. It retries a server error, a timeout, `408` and `429` with the workflow's bounded exponential policy, then reports the version delivered, or not delivered with the technical cause, as the identity holding `Trips.ReportDelivery`. A failed report fails the run and leaves the version Sending.
5. It completes the message once it is read, and dead-letters an unreadable one as `TripDispatchUnreadable`.

It sets no new pattern, so this record only says that the pattern is applied a fourth time. The one difference from ADR 0034 is that the credential belongs to one endpoint, the Driver app's, not to one per party.

## Consequences

- The worker is a second implementation of the workflow in `src/Integration/TripDispatchLogicApp/`. A change to the workflow's steps, retry policy or tracked properties is made in both; CI checks the workflow's definition and the worker's unit tests check the same list.
- It runs only locally. `azd` never deploys it.
- Because the message is completed before the send, a run that fails after it is not redelivered. The Planner sends the version again from the Trip detail.
