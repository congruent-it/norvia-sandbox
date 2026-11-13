# E3 Master-data sync

Finance changes a record the ERP owns, and the change is replicated to the TMS through Azure Integration Services. The ERP owns the six master-data entities (ADR 0002, 0006, 0013, 0014) and the TMS holds replicated read copies; neither calls the other (ADR 0001, ADR 0003). Terms are in [GLOSSARY.md](../../../GLOSSARY.md); the design is [ADR 0050](../../adr/0050-the-erp-replicates-each-master-data-change-to-the-tms-as-one-whole-record-event-with-a-revision.md), and locally [ADR 0051](../../adr/0051-locally-a-hosted-worker-plays-the-e3-master-data-sync-logic-app.md) plays the Logic App.

| | |
|---|---|
| **Trigger** | Finance creates or edits a record in the ERP |
| **Outcome** | The TMS's copy is the ERP's record, at the same Master-data revision |
| **Integrations touched** | Service Bus (`master-data-events`), the master-data sync Logic App, the TMS API |
| **Role** | Finance edits. The Logic App's identity holds `MasterData.Replace` on the TMS API and nothing else |

## Process model

1. **Edit.** The ERP API's `PUT /v1/shippers/{id}` (role `MasterData.Manage`) creates or edits the Shipper. A new Shipper starts at Master-data revision 1; an edit raises it by one. A repeat of what the Shipper already is changes and publishes nothing.
2. **Publish.** In the same transaction the ERP writes the whole Shipper to its outbox as the `Shipper` event ([shipper.v1](../../../contracts/canonical-model/shipper.v1.schema.json)) on `master-data-events`, under a correlation ID created for the change, with `flow` `master-data` and the Shipper as `partyId`.
3. **Replicate.** The Logic App's `master-data-sync` subscription admits `Shipper` events. It maps the event to `PUT /v1/shippers/{id}` on the TMS API with the name, the active flag and the revision, with the event's correlation ID, as its own identity, retrying a server error, a timeout, `408` and `429` three times (5 s up to 20 s).
4. **Apply.** The TMS upserts the Shipper, so a new one appears. It keeps the latest revision it applied and answers `200` with `applied: false`, changing nothing, to a revision that is not higher.

A Shipper is never deleted, only made inactive. Making one inactive stores `Active = false` and nothing else.

## Failure behaviour

| What fails | Result |
|---|---|
| The TMS doesn't answer, or answers a server error, `408` or `429`, after the retries | Dead-lettered as `TmsUnavailable` |
| The TMS refuses (any other client error, a conflict included) | Dead-lettered as `TmsCallRefused`, with the TMS's message |
| The message isn't a `Shipper` event the TMS can replace a record from | Dead-lettered as `MasterDataUnreadable`, nothing sent |
| An older or repeated revision arrives | `2xx`, nothing changed |

A dead-lettered message is resubmitted from the Integration console to the `master-data-sync` subscription only.

## Implementation

| Part | Where |
|---|---|
| Revision, edit and silent seed reload | [Shipper](../../../src/Erp/Norvia.Erp.Domain/Shippers/Shipper.cs), migration `ShipperRevision` |
| Save use case and event | [SaveShipperHandler](../../../src/Erp/Norvia.Erp.Application/MasterData/Shippers/Save/SaveShipperHandler.cs), [ShipperV1](../../../src/Erp/Norvia.Erp.Application/MasterData/Shippers/ShipperV1.cs) |
| ERP API | [MasterDataEndpoints](../../../src/Erp/Norvia.Erp.Api/MasterData/MasterDataEndpoints.cs) |
| TMS command, API and replica store | [ReplaceShipperHandler](../../../src/Tms/Norvia.Tms.Application/Shippers/Replace/ReplaceShipperHandler.cs), [ShipperEndpoints](../../../src/Tms/Norvia.Tms.Api/Shippers/ShipperEndpoints.cs), [ReplicatedShipperDirectory](../../../src/Tms/Norvia.Tms.Infrastructure/Shippers/ReplicatedShipperDirectory.cs) (the `ShipperCopies` table laid over the seed file) |
| Logic App | [src/Integration/MasterDataSyncLogicApp](../../../src/Integration/MasterDataSyncLogicApp/), [master-data-sync-logic-app.bicep](../../../infra/estate/master-data-sync-logic-app.bicep), `infra/deploy-master-data-sync-logic-app.ps1` |
| Local worker | [LocalMasterDataSyncWorker](../../../src/Integration/Norvia.Integration.LocalMasterDataSyncWorker/) |

## Deferred

- **The other five entities.** Carriers, Rate Cards, Carrier Rate Cards, Vehicles and Drivers still come from the seed file in both applications. They follow the Shipper's event, filter and command. The ERP has no Vehicle or Driver aggregate yet. A Rate Card replacement carries its full list of dated versions and replaces the TMS's list; a replaced Rate Card changes only what is rated after it arrives.
- **The ERP UI's create and edit forms.** Today only the ERP API edits a Shipper. The UI will change records through the API as its own identity holding `MasterData.Manage` (ADR 0028's split); that role is not yet defined on the ERP API's registration in `infra/estate/identities.bicep`.
- **Bulk import, an audit trail and an approval step.**
- **Flagging a Trip that uses a Vehicle or Driver made inactive** (P11).
- **The as-found toggle.** The existing two cover E3 once it is listed.
