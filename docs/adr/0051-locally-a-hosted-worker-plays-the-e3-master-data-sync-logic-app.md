# Locally, a hosted worker plays the E3 master-data sync Logic App

The E3 master-data sync Logic App picks up the ERP's master-data events from its own `master-data-sync` subscription on the `master-data-events` topic and replaces the record in the TMS with the TMS API. Locally there is no Logic Apps runtime, so under Aspire a .NET worker, `Norvia.Integration.LocalMasterDataSyncWorker`, does the Logic App's work, as ADR 0043 does for E1:

1. It reads the same subscription the estate gives the Logic App, with the same resubmit-aware filter (`eventType IN ('Shipper', 'Carrier')`), from the Service Bus emulator.
2. It maps the event to the TMS command (`PUT /v1/shippers/{id}` with the name, the active flag and the Master-data revision) and calls it with the event's correlation ID and a token of the one identity holding `MasterData.Replace`, signed with the AppHost's local key.
3. It retries a server error, a timeout, `408` and `429` with a bounded exponential policy set to the workflow's numbers (3 retries, 5 s up to 20 s), and any other client error not at all.
4. It completes the message when the TMS answers `2xx`, a revision that was not applied included. Otherwise it dead-letters it with `TmsUnavailable` (retries used up), `TmsCallRefused` (a refusal) or, for a message that isn't an event the TMS can replace a record from, `MasterDataUnreadable`, with the TMS's message as the description.

The tracking contract's keys go into the logging scope under the names the workflow's tracked properties use (ADR 0027).

## Considered Options

- **No local flow:** an edit in the ERP would never reach the TMS locally.
- **Having the ERP call the TMS:** it would break ADR 0001 and ADR 0003.

## Consequences

- The worker is a second implementation of the workflow in `src/Integration/MasterDataSyncLogicApp/`. A change to the workflow's steps, retry policy or failure codes must be made in both. CI checks the workflow's definition and the worker's unit tests check the same list.
- It writes no Logic App run history; locally the trail is in the Aspire dashboard's telemetry.
- It runs only locally. `azd` never deploys it.
