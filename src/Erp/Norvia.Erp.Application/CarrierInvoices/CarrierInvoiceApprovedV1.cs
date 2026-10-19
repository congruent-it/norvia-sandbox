using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.CarrierInvoices;

/// <summary>
/// The Canonical Model event <c>CarrierInvoiceApproved</c>, version 1 (contracts/canonical-model/carrier-invoice-approved.v1.schema.json):
/// every line of a Carrier invoice is Matched, Accepted or Rejected. Its correlation ID is the one API Management created for the Carrier
/// invoice. It carries what the Carrier invoice outcome Logic App needs to tell the Carrier, so it looks nothing up.
/// </summary>
public sealed record CarrierInvoiceApprovedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CarrierId,
    string InvoiceNumber,
    decimal ApprovedAmount,
    IReadOnlyList<CarrierInvoiceApprovedLine> Lines)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierInvoiceApproved";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.InvoicingEvents;

    public static CarrierInvoiceApprovedV1 Of(CarrierInvoice invoice, Guid eventId, DateTimeOffset occurredAt) => new(
        eventId,
        occurredAt,
        invoice.CorrelationId,
        invoice.CarrierId.Value,
        invoice.InvoiceNumber,
        invoice.ApprovedAmount ?? throw new InvalidOperationException($"Carrier invoice {invoice.InvoiceNumber} is not Approved."),
        [.. invoice.Lines.Select(line => new CarrierInvoiceApprovedLine(line.CarrierBookingReference, line.Status.ToString(), line.Amount, line.AcceptedAmount, line.Remark))]);

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.CarrierInvoices),
        new(TrackingKeys.PartyId, CarrierId),
        new(TrackingKeys.DocumentNumber, InvoiceNumber),
    ];
}

/// <param name="Result"><c>Matched</c>, <c>Accepted</c> or <c>Rejected</c>.</param>
/// <param name="InvoicedAmount">What the Carrier claimed for the line.</param>
/// <param name="AcceptedAmount">What the line contributes to the approved amount; null for a Rejected line.</param>
/// <param name="Remark">Finance's remark on an adjusted amount, or its reason for a rejection; null for a Matched line.</param>
public sealed record CarrierInvoiceApprovedLine(
    string CarrierBookingReference,
    string Result,
    decimal InvoicedAmount,
    decimal? AcceptedAmount,
    string? Remark);
