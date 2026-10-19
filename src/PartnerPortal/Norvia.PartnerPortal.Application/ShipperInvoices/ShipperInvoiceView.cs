using Norvia.PartnerPortal.Domain.ShipperInvoices;

namespace Norvia.PartnerPortal.Application.ShipperInvoices;

/// <summary>An invoice or Credit note in the Shipper's inbox.</summary>
/// <param name="Credit">What a Credit note corrects; null on an invoice.</param>
public sealed record ShipperInvoiceView(
    string DocumentNumber,
    InvoiceDocumentType DocumentType,
    DateTimeOffset ReceivedAt,
    DateOnly DocumentDate,
    CreditedLine? Credit,
    IReadOnlyList<ShipperInvoiceLine> Lines,
    decimal TotalWithoutVat,
    decimal Vat,
    decimal TotalWithVat,
    string CorrelationId)
{
    public static ShipperInvoiceView From(ShipperInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var content = invoice.Content;
        return new(
            invoice.DocumentNumber, content.DocumentType, invoice.ReceivedAt, content.DocumentDate, content.Credit, content.Lines,
            content.TotalWithoutVat, content.Vat, content.TotalWithVat, invoice.CorrelationId);
    }
}
