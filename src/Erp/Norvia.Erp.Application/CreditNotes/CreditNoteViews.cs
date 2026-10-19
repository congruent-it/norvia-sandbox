using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CreditNotes;

namespace Norvia.Erp.Application.CreditNotes;

internal static class CreditNoteViews
{
    public static CreditNoteView Of(CreditNote note, string shipperName) => new(
        note.Id.Value,
        note.CreditNoteDate,
        new ShipperOnInvoiceView(note.ShipperId.Value, shipperName),
        note.InvoiceNumber.Value,
        note.TransportOrderNumber,
        note.ShipperReference,
        note.ShipmentNumber,
        note.DeliveryDate,
        FreightCharge.Currency,
        note.AmountWithoutVat,
        note.Vat,
        note.TotalWithVat,
        note.Reason,
        note.IssuedAt,
        note.CorrelationId);
}
