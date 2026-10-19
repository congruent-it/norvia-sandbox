using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;

/// <summary>
/// The Canonical Model event <c>ShipperInvoiceNotDelivered</c>, version 1 (contracts/canonical-model/shipper-invoice-not-delivered.v1.schema.json):
/// a Shipper invoice never reached the Shipper. Published under the invoice's correlation ID, for monitoring.
/// </summary>
/// <param name="DocumentType"><c>INVOICE</c> or <c>CREDIT_NOTE</c>.</param>
/// <param name="StatusCode">The Shipper endpoint's HTTP status code, or null when the failure had none, such as a timeout.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record ShipperInvoiceNotDeliveredV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string DocumentType,
    string DocumentNumber,
    string ShipperId,
    int? StatusCode,
    string Message)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public const string InvoiceDocumentType = "INVOICE";

    public const string CreditNoteDocumentType = "CREDIT_NOTE";

    public override string EventType => "ShipperInvoiceNotDelivered";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.InvoicingEvents;

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.Invoicing),
        new(TrackingKeys.PartyId, ShipperId),
        new(TrackingKeys.DocumentNumber, DocumentNumber),
    ];
}
