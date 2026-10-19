using Ardalis.Specification;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Domain.CreditNotes;

/// <summary>The Credit notes already issued on one line of a Shipper invoice, which the next one may not exceed.</summary>
public sealed class CreditNotesOfInvoiceLineSpec : Specification<CreditNote>
{
    public CreditNotesOfInvoiceLineSpec(InvoiceNumber invoiceNumber, string transportOrderNumber) =>
        Query.AsNoTracking().Where(note => note.InvoiceNumber == invoiceNumber && note.TransportOrderNumber == transportOrderNumber);
}
