using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>What a Carrier invoice carries when it is received, as the domain takes it.</summary>
/// <param name="CarrierId">The Carrier, from the caller's token; never from what the Carrier wrote.</param>
/// <param name="InvoiceNumber">The Carrier's own invoice number, unique per Carrier.</param>
public sealed record CarrierInvoiceData(
    CarrierId CarrierId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    IReadOnlyList<CarrierInvoiceLineData> Lines,
    string CorrelationId);

public sealed record CarrierInvoiceLineData(string CarrierBookingReference, decimal Amount, string? Description);
