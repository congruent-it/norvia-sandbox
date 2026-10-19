using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.CarrierInvoices.ReportNotDelivered;

/// <summary>
/// The Canonical Model event <c>CarrierInvoiceOutcomeNotDelivered</c>, version 1
/// (contracts/canonical-model/carrier-invoice-outcome-not-delivered.v1.schema.json): the outcome of an Approved Carrier invoice never reached the
/// Carrier. Published under the Carrier invoice's correlation ID, for monitoring.
/// </summary>
/// <param name="StatusCode">The Carrier endpoint's HTTP status code, or null when the failure had none, such as a timeout.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record CarrierInvoiceOutcomeNotDeliveredV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CarrierId,
    string InvoiceNumber,
    int? StatusCode,
    string Message)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierInvoiceOutcomeNotDelivered";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.InvoicingEvents;

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.CarrierInvoices),
        new(TrackingKeys.PartyId, CarrierId),
        new(TrackingKeys.DocumentNumber, InvoiceNumber),
    ];
}
