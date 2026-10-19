using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.ShipperInvoices;

/// <summary>
/// The Canonical Model event <c>ShipperInvoiceIssued</c>, version 1 (contracts/canonical-model/shipper-invoice-issued.v1.schema.json): the
/// whole invoice with its lines, totals and Shipper. Its correlation ID is the one the ERP created for the invoice.
/// </summary>
public sealed record ShipperInvoiceIssuedV1(Guid EventId, DateTimeOffset OccurredAt, string CorrelationId, ShipperInvoiceView Invoice)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ShipperInvoiceIssued";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.InvoicingEvents;

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.Invoicing),
        new(TrackingKeys.PartyId, Invoice.Shipper.ShipperId),
    ];
}
