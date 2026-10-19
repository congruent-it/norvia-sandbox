namespace Norvia.Erp.Application.CarrierInvoices;

/// <summary>A Carrier invoice as the ERP answers with it, in the Canonical Model's terms.</summary>
public sealed record CarrierInvoiceView(
    string CarrierId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string Currency,
    decimal Total,
    IReadOnlyList<CarrierInvoiceLineView> Lines,
    string CorrelationId,
    DateTimeOffset ReceivedAt,
    string Status,
    decimal? ApprovedAmount,
    DateTimeOffset? ApprovedAt);

/// <param name="Result">What matching found: <c>Matched</c>, <c>PriceMismatch</c>, <c>UnknownBooking</c> or <c>Duplicate</c>.</param>
/// <param name="Status"><c>Matched</c> or <c>ToDecide</c>.</param>
/// <param name="CarrierBookingNumber">The Carrier cost the line was matched against; null for an Unknown booking.</param>
/// <param name="AcceptedAmount">What the line contributes to the approved amount; null while it waits for Finance.</param>
public sealed record CarrierInvoiceLineView(
    string CarrierBookingReference,
    decimal Amount,
    string? Description,
    string Result,
    string Status,
    string? CarrierBookingNumber,
    decimal? AcceptedAmount);

/// <summary>A Carrier invoice in the list Finance reads, with the Carrier's name.</summary>
public sealed record CarrierInvoiceListItem(string CarrierName, CarrierInvoiceView Invoice);
