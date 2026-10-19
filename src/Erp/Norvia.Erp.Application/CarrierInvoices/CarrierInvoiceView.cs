using Norvia.Erp.Application.CarrierCosts;

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
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ToldAt = null,
    DateTimeOffset? NotToldAt = null,
    int? NotToldStatusCode = null,
    string? NotToldMessage = null);

/// <param name="Result">What matching found: <c>Matched</c>, <c>PriceMismatch</c>, <c>UnknownBooking</c> or <c>Duplicate</c>.</param>
/// <param name="Status"><c>Matched</c>, <c>ToDecide</c>, <c>Accepted</c> or <c>Rejected</c>.</param>
/// <param name="CarrierBookingNumber">The Carrier cost the line was matched against; null for an Unknown booking.</param>
/// <param name="AcceptedAmount">What the line contributes to the approved amount; null while it waits for Finance and for a Rejected line.</param>
/// <param name="Remark">Finance's remark on an adjusted amount, or its reason for a rejection; null otherwise.</param>
public sealed record CarrierInvoiceLineView(
    string CarrierBookingReference,
    decimal Amount,
    string? Description,
    string Result,
    string Status,
    string? CarrierBookingNumber,
    decimal? AcceptedAmount,
    string? Remark = null);

/// <summary>A Carrier invoice in the list Finance reads, with the Carrier's name.</summary>
public sealed record CarrierInvoiceListItem(string CarrierName, CarrierInvoiceView Invoice);

/// <summary>
/// A Received Carrier invoice in the list Finance decides from, with the Carrier cost of each line that has one, by Carrier booking
/// number, so a line can be shown next to what Norvia agreed to pay.
/// </summary>
public sealed record CarrierInvoiceToDecide(string CarrierName, CarrierInvoiceView Invoice, IReadOnlyDictionary<string, CarrierCostView> Costs);
