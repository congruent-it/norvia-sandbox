using Norvia.Erp.Application.ShipperInvoices;

namespace Norvia.Erp.Application.CreditNotes;

/// <summary>A Credit note as the ERP shows it and publishes it, in the Canonical Model's terms.</summary>
/// <param name="InvoiceNumber">The Shipper invoice credited.</param>
/// <param name="TransportOrderNumber">The Transport Order number of the invoice line credited.</param>
public sealed record CreditNoteView(
    string CreditNoteNumber,
    DateOnly CreditNoteDate,
    ShipperOnInvoiceView Shipper,
    string InvoiceNumber,
    string TransportOrderNumber,
    string ShipperReference,
    string ShipmentNumber,
    DateOnly DeliveryDate,
    string Currency,
    decimal TotalWithoutVat,
    decimal Vat,
    decimal TotalWithVat,
    string Reason,
    DateTimeOffset IssuedAt,
    string CorrelationId);
