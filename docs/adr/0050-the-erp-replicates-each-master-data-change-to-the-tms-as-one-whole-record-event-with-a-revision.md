# The ERP replicates each master-data change to the TMS as one whole-record event with a Master-data revision

The ERP owns Shippers, Carriers, Rate Cards, Carrier Rate Cards, Vehicles and Drivers (ADR 0002, ADR 0006, ADR 0013, ADR 0014) and the TMS holds replicated read copies. Finance edits a record in the ERP and the change reaches the TMS through Azure Integration Services only (ADR 0001, ADR 0003): the ERP does not call the TMS.

- **One event per changed record, carrying the whole record**, as ADR 0015 does for Trips. The TMS replaces its copy and never patches it.
- **A Master-data revision.** The ERP raises a number on every edit; the seed file's records are at revision 1. The TMS keeps the latest revision it applied and answers `2xx`, changing nothing, to one that is not higher. A redelivery, a resubmit from the Integration console or two edits that arrive out of order cannot move a copy backwards.
- **One `master-data-events` topic**, the entity type as the event type (`Shipper` is the first), with a `monitoring` subscription and a `master-data-sync` subscription for the E3 Logic App.
- **The TMS command upserts**, so a new record appears in the TMS, and it never edits one on its own. The TMS keeps the replicas apart from what it owns, such as availability (ADR 0014).
- **The seed file stays a silent first load** in both applications, at revision 1 with the same IDs. Events are published on edits only, and a seed reload never overwrites an edited record.
- **Edits go through the ERP API**, as ADR 0028 does for the TMS: the ERP UI reads in process and changes through Finance-role endpoints, idempotent on content (a repeat of what the record already is changes and publishes nothing). This is the one exception to "the API takes events and reports, not people's commands".
- **Never deleted, only inactive.**

## Considered Options

- **A patch event with the changed fields:** smaller, but a lost or reordered patch leaves the copy wrong for good, and the revision rule could not make a redelivery harmless.
- **The TMS asking the ERP for the record:** it would break the rule that applications meet only through the integration layer.
- **One topic per entity:** a subscription and a Logic App per entity for the same flow, which a filter on the event type does with one.

## Consequences

- A replaced Rate Card changes only what is rated after it arrives; Orders already Rated keep their Freight Charge. Re-rating them is a separate issue.
- A Shipper, Vehicle or Driver made inactive only stores `Active = false`. Nothing else in the TMS is edited because of it.
- The TMS reads its replicas from its database on every call, so every instance sees a replacement at once.
- Built: all six entities, each with the same event, topic, filter and command. A Vehicle or Driver made inactive stores only `Active = false`; a Trip that uses it is flagged Needs a Vehicle or Needs a Driver (P11) and the TMS's Rosters and Off the road days are untouched.
- Whether an edit changed the record is the aggregate's decision, not the handler's: each master-data aggregate's `Edit` returns false, and changes nothing, when the content is already the record's, and the save handler publishes only when it returned true. Every entity works this way. A Driver's licences are compared whatever their order. A Rate Card compares its whole list of dated versions, whatever their order. A Rate Card's versions must not overlap in time, and the ERP and the TMS each refuse a list in which they do; a Shipper has one Rate Card and a Carrier one Carrier Rate Card.
