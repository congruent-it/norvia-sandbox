using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.CreditNotes;

/// <summary>
/// The Canonical Model event <c>CreditNoteIssued</c>, version 1 (contracts/canonical-model/credit-note-issued.v1.schema.json): the whole
/// Credit note with the invoice and line it credits, amounts, reason and Shipper. Its correlation ID is the one the ERP created for the
/// Credit note.
/// </summary>
public sealed record CreditNoteIssuedV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, CreditNoteView CreditNote)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CreditNoteIssued";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.InvoicingEvents;

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.Invoicing),
        new(TrackingKeys.PartyId, CreditNote.Shipper.ShipperId),
        new(TrackingKeys.DocumentNumber, CreditNote.CreditNoteNumber),
    ];
}
