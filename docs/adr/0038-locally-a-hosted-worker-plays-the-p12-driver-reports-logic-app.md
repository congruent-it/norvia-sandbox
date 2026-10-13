# Locally, a hosted worker plays the P12 Driver reports Logic App

The P12 Driver reports Logic App reads the Driver app's events from its own `driver-reports` subscription, calls the TMS for each report and tells the Driver app what became of it ([ADR 0016](0016-driver-reports-travel-as-driver-app-events-in-order-per-trip.md)). Locally there is no Logic Apps runtime, so under Aspire a .NET worker, `Norvia.Integration.LocalDriverReportsWorker`, does its work, as [ADR 0036](0036-locally-a-hosted-worker-plays-the-p11-trip-dispatch-logic-app.md) does for P11:

1. It reads the same session-enabled subscription the estate gives the Logic App, with the same resubmit-aware filter (`eventType = 'TripStartReported' OR eventType = 'StopCompletedReported'`), one message at a time per session, so a Trip's reports are handled in order; different Trips are read side by side.
2. It calls the TMS operation the event names with the Driver ID and Driver app event ID from the event and the event's correlation ID: Start a Trip for `TripStartReported` as the one identity holding `Trips.Start`, and Record a Tracking event of Own fleet for `StopCompletedReported` as the same identity holding `TrackingEvents.Record`, with a token signed by the AppHost's local key.
3. It retries a server error, a timeout, `408` and `429` with the workflow's bounded exponential policy.
4. It settles the message before it reports: completed when the TMS recorded the report, dead-lettered with the failure code (the TMS's own for a refusal, `TmsUnavailable` for a TMS that stayed down) and the readable message otherwise, so the session moves on. An unreadable message is dead-lettered as `DriverReportUnreadable`.
5. It posts the outcome (Recorded, Refused, Not recorded) to the Driver app's outcome endpoint as the identity holding `DriverReports.ReportOutcome`. If the Driver app can't be reached after the retries the run fails and the report stays Sending.

## Consequences

- The worker is a second implementation of the workflow in `src/Integration/DriverReportsLogicApp/`. A change to the workflow's steps, retry policy or tracked properties is made in both; CI checks the workflow's definition and the worker's unit tests check the same list.
- It runs only locally. `azd` never deploys it.
- Because the message is settled before the outcome is reported, a run that fails to report is not redelivered. The TMS has the report, and the Driver sees it Sending.
