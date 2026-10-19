namespace Norvia.PartnerPortal.Domain.ShipperInvoices;

/// <summary>What an invoice or Credit note says, as the Shipper invoice format carries it (E1). It never names the Carrier or the Shipment number (ADR 0011).</summary>
/// <param name="DocumentNumber">The key of the document: <c>INV-2030-000001</c> or <c>CN-2030-000001</c>.</param>
/// <param name="DocumentDate">The date of the document, local.</param>
/// <param name="Credit">What a Credit note corrects; null on an invoice.</param>
/// <param name="TotalWithoutVat">The sum of the line totals, in EUR.</param>
public sealed record ShipperInvoiceContent(
    InvoiceDocumentType DocumentType,
    string DocumentNumber,
    DateOnly DocumentDate,
    CreditedLine? Credit,
    IReadOnlyList<ShipperInvoiceLine> Lines,
    decimal TotalWithoutVat,
    decimal Vat,
    decimal TotalWithVat);

/// <summary>The invoice and the line of it a Credit note corrects, and why.</summary>
public sealed record CreditedLine(string InvoiceNumber, string TransportOrderNumber, string Reason);

/// <param name="BookingRef">The Shipper's own reference of the booking, so the Shipper recognises the Transport Order.</param>
public sealed record ShipperInvoiceLine(
    string TransportOrderNumber,
    string BookingRef,
    DateOnly DeliveryDate,
    IReadOnlyList<ShipperChargeLine> ChargeLines,
    decimal LineTotal);

public sealed record ShipperChargeLine(ChargeCode Code, decimal Amount);
