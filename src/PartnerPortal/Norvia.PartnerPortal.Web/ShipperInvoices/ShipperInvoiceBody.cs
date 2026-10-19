namespace Norvia.PartnerPortal.Web.ShipperInvoices;

/// <summary>The invoice or Credit note in the Shipper invoice format (E1), as the Shipper invoice Logic App sends it.</summary>
public sealed record ShipperInvoiceBody(
    string? DocumentType,
    string? DocumentNumber,
    string? DocumentDate,
    string? CreditedInvoiceNumber,
    string? CreditedTransportOrderNumber,
    string? Reason,
    IReadOnlyList<ShipperInvoiceLineBody?>? Lines,
    string? Currency,
    decimal? TotalWithoutVat,
    decimal? Vat,
    decimal? TotalWithVat);

public sealed record ShipperInvoiceLineBody(
    string? TransportOrderNumber,
    string? BookingRef,
    string? DeliveryDate,
    IReadOnlyList<ShipperChargeLineBody?>? ChargeLines,
    decimal? LineTotal);

public sealed record ShipperChargeLineBody(string? Code, decimal? Amount);
