using Ardalis.Specification;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Domain.CreditNotes;

/// <summary>Every Credit note, the newest first, for the list Finance reads; or only those of one Shipper invoice.</summary>
public sealed class CreditNotesNewestFirstSpec : Specification<CreditNote>
{
    public CreditNotesNewestFirstSpec(InvoiceNumber? invoiceNumber = null)
    {
        Query.AsNoTracking();
        if (invoiceNumber is { } number)
        {
            Query.Where(note => note.InvoiceNumber == number);
        }

        Query.OrderByDescending(note => note.IssuedAt).ThenByDescending(note => note.Id);
    }
}
