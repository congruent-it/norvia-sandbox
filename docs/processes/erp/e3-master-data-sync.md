# E3 Master-data sync

Finance changes a record the ERP owns, and the change is replicated to the TMS through Azure Integration Services. The ERP owns the six master-data entities (ADR 0002, 0006, 0013, 0014) and the TMS holds replicated read copies; neither calls the other (ADR 0001, ADR 0003). Terms are in [GLOSSARY.md](../../../GLOSSARY.md); the design is [ADR 0050](../../adr/0050-the-erp-replicates-each-master-data-change-to-the-tms-as-one-whole-record-event-with-a-revision.md), and locally [ADR 0051](../../adr/0051-locally-a-hosted-worker-plays-the-e3-master-data-sync-logic-app.md) plays the Logic App.

| | |
|---|---|
| **Trigger** | Finance creates or edits a record in the ERP |
| **Outcome** | The TMS's copy is the ERP's record, at the same Master-data revision |
| **Integrations touched** | Service Bus (`master-data-events`), the master-data sync Logic App, the TMS API |
| **Role** | Finance edits. The Logic App's identity holds `MasterData.Replace` on the TMS API and nothing else |

## Process model

1. **Edit.** The ERP API's `PUT /v1/shippers/{id}`, `PUT /v1/carriers/{id}`, `PUT /v1/rate-cards/{id}` or `PUT /v1/carrier-rate-cards/{id}` (role `MasterData.Manage`) creates or edits the record. A Rate Card or Carrier Rate Card is saved with its whole list of dated versions, which must not overlap in time, and a Shipper or Carrier has one. A new record starts at Master-data revision 1; an edit raises it by one. A repeat of what the record already is, a card's versions compared whatever their order, changes and publishes nothing.
2. **Publish.** In the same transaction the ERP writes the whole record to its outbox as the `Shipper` event ([shipper.v1](../../../contracts/canonical-model/shipper.v1.schema.json)), the `Carrier` event ([carrier.v1](../../../contracts/canonical-model/carrier.v1.schema.json)), the `RateCard` event ([rate-card.v1](../../../contracts/canonical-model/rate-card.v1.schema.json)) or the `CarrierRateCard` event ([carrier-rate-card.v1](../../../contracts/canonical-model/carrier-rate-card.v1.schema.json)) on `master-data-events`, under a correlation ID created for the change, with `flow` `master-data` and the record's ID as `partyId`.
3. **Replicate.** The Logic App's `master-data-sync` subscription admits `Shipper`, `Carrier`, `RateCard` and `CarrierRateCard` events. It maps the event to `PUT /v1/shippers/{id}`, `/v1/carriers/{id}`, `/v1/rate-cards/{id}` or `/v1/carrier-rate-cards/{id}` on the TMS API with the record but for its ID (a Shipper's or Carrier's name, active flag and revision; a card's party, revision and full list of versions), with the event's correlation ID, as its own identity, retrying a server error, a timeout, `408` and `429` three times (5 s up to 20 s).
4. **Apply.** The TMS upserts the record, so a new one appears; a Rate Card or Carrier Rate Card replaces the TMS's whole list of versions. It keeps the latest revision it applied and answers `200` with `applied: false`, changing nothing, to a revision that is not higher. A replaced card changes only what is rated after it arrives: a Transport Order already Rated keeps its Freight Charge, and re-rating is [#908](https://github.com/congruent-it/norvia-sandbox/issues/908).

A Shipper or Carrier is never deleted, only made inactive. Making one inactive stores `Active = false` and nothing else.

## Failure behaviour

| What fails | Result |
|---|---|
| The TMS doesn't answer, or answers a server error, `408` or `429`, after the retries | Dead-lettered as `TmsUnavailable` |
| The TMS refuses (any other client error, a conflict included) | Dead-lettered as `TmsCallRefused`, with the TMS's message |
| The message isn't a `Shipper`, `Carrier`, `RateCard` or `CarrierRateCard` event the TMS can replace a record from | Dead-lettered as `MasterDataUnreadable`, nothing sent |
| An older or repeated revision arrives | `2xx`, nothing changed |
| A card's versions overlap in time, or can't be read | Dead-lettered as `TmsCallRefused` (`RateCardVersionsOverlap` or `MasterDataUnreadable` in the TMS's message); the ERP refuses such a card before it publishes |

A dead-lettered message is resubmitted from the Integration console to the `master-data-sync` subscription only.

## Implementation

| Part | Where |
|---|---|
| Revision, edit and silent seed reload | [Shipper](../../../src/Erp/Norvia.Erp.Domain/Shippers/Shipper.cs), migration `ShipperRevision`; [Carrier](../../../src/Erp/Norvia.Erp.Domain/Carriers/Carrier.cs), migration `CarrierRevision` |
| Save use case and event | [SaveShipperHandler](../../../src/Erp/Norvia.Erp.Application/MasterData/Shippers/Save/SaveShipperHandler.cs), [ShipperV1](../../../src/Erp/Norvia.Erp.Application/MasterData/Shippers/ShipperV1.cs); [SaveCarrierHandler](../../../src/Erp/Norvia.Erp.Application/MasterData/Carriers/Save/SaveCarrierHandler.cs), [CarrierV1](../../../src/Erp/Norvia.Erp.Application/MasterData/Carriers/CarrierV1.cs) |
| ERP API | [MasterDataEndpoints](../../../src/Erp/Norvia.Erp.Api/MasterData/MasterDataEndpoints.cs) |
| Rate Card and Carrier Rate Card | ERP: [RateCard](../../../src/Erp/Norvia.Erp.Domain/RateCards/RateCard.cs), [CarrierRateCard](../../../src/Erp/Norvia.Erp.Domain/RateCards/CarrierRateCard.cs) and [RateCardVersionRules](../../../src/Erp/Norvia.Erp.Domain/RateCards/RateCardVersionRules.cs) (revision, `Edit`, overlap rule; migration `RateCardRevision`), [SaveRateCardHandler](../../../src/Erp/Norvia.Erp.Application/MasterData/RateCards/Save/SaveRateCardHandler.cs), [SaveCarrierRateCardHandler](../../../src/Erp/Norvia.Erp.Application/MasterData/RateCards/SaveCarrier/SaveCarrierRateCardHandler.cs). TMS: [ReplaceRateCardHandler](../../../src/Tms/Norvia.Tms.Application/RateCards/Replace/ReplaceRateCardHandler.cs), [ReplaceCarrierRateCardHandler](../../../src/Tms/Norvia.Tms.Application/RateCards/ReplaceCarrier/ReplaceCarrierRateCardHandler.cs), [RateCardEndpoints](../../../src/Tms/Norvia.Tms.Api/RateCards/RateCardEndpoints.cs), [ReplicatedRateCardDirectory](../../../src/Tms/Norvia.Tms.Infrastructure/RateCards/ReplicatedRateCardDirectory.cs) and [ReplicatedCarrierRateCardDirectory](../../../src/Tms/Norvia.Tms.Infrastructure/RateCards/ReplicatedCarrierRateCardDirectory.cs) (the `RateCardCopies` and `CarrierRateCardCopies` tables laid over the seed file) |
| TMS command, API and replica store | [ReplaceCarrierHandler](../../../src/Tms/Norvia.Tms.Application/Carriers/Replace/ReplaceCarrierHandler.cs), [CarrierEndpoints](../../../src/Tms/Norvia.Tms.Api/Carriers/CarrierEndpoints.cs), [ReplicatedCarrierDirectory](../../../src/Tms/Norvia.Tms.Infrastructure/Carriers/ReplicatedCarrierDirectory.cs) (the `CarrierCopies` table), and for the Shipper [ReplaceShipperHandler](../../../src/Tms/Norvia.Tms.Application/Shippers/Replace/ReplaceShipperHandler.cs), [ShipperEndpoints](../../../src/Tms/Norvia.Tms.Api/Shippers/ShipperEndpoints.cs), [ReplicatedShipperDirectory](../../../src/Tms/Norvia.Tms.Infrastructure/Shippers/ReplicatedShipperDirectory.cs) (the `ShipperCopies` table laid over the seed file) |
| Logic App | [src/Integration/MasterDataSyncLogicApp](../../../src/Integration/MasterDataSyncLogicApp/), [master-data-sync-logic-app.bicep](../../../infra/estate/master-data-sync-logic-app.bicep), `infra/deploy-master-data-sync-logic-app.ps1` |
| Local worker | [LocalMasterDataSyncWorker](../../../src/Integration/Norvia.Integration.LocalMasterDataSyncWorker/) |

## Deferred

- **Vehicles and Drivers.** They still come from the seed file in both applications. They follow the Shipper's and the Carrier's event, filter and command, and the aggregate's `Edit` says whether an edit changed anything ([ADR 0050](../../adr/0050-the-erp-replicates-each-master-data-change-to-the-tms-as-one-whole-record-event-with-a-revision.md)). The ERP has no Vehicle or Driver aggregate yet.
- **Re-rating Orders after a Rate Card is replaced** ([#908](https://github.com/congruent-it/norvia-sandbox/issues/908)).
- **The ERP UI's create and edit forms.** Today only the ERP API edits a Shipper. The UI will change records through the API as its own identity holding `MasterData.Manage` (ADR 0028's split); that role is not yet defined on the ERP API's registration in `infra/estate/identities.bicep`.
- **Bulk import, an audit trail and an approval step.**
- **Flagging a Trip that uses a Vehicle or Driver made inactive** (P11).
- **The as-found toggle.** The existing two cover E3 once it is listed.
