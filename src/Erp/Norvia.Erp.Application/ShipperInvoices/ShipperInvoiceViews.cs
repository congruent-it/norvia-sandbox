using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Application.ShipperInvoices;

internal static class ShipperInvoiceViews
{
    public static ShipperInvoiceView Of(ShipperInvoice invoice, string shipperName) => new(
        invoice.Id.Value,
        invoice.InvoiceDate,
        new ShipperOnInvoiceView(invoice.ShipperId.Value, shipperName),
        [.. invoice.Lines.Select(Of)],
        FreightCharge.Currency,
        invoice.TotalWithoutVat,
        invoice.Vat,
        invoice.TotalWithVat,
        invoice.IssuedAt,
        invoice.CorrelationId,
        invoice.Status.ToString(),
        invoice.SentAt);

    private static ShipperInvoiceLineView Of(ShipperInvoiceLine line) => new(
        line.TransportOrderNumber,
        line.ShipperReference,
        line.ShipmentNumber,
        DateOnly.FromDateTime(line.DeliveryTime.DateTime),
        [.. line.ChargeLines.Select(chargeLine => new InvoiceChargeLineView(chargeLine.Type.ToString(), chargeLine.Amount))],
        line.Total);
}
