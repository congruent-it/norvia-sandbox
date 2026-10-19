using Ardalis.Specification;

namespace Norvia.Erp.Domain.ShipperInvoices;

/// <summary>The invoice number sequence of one calendar year, tracked: the issuing transaction takes the next number from it.</summary>
public sealed class InvoiceNumberSequenceOfYearSpec : Specification<InvoiceNumberSequence>
{
    public InvoiceNumberSequenceOfYearSpec(int year) => Query.Where(sequence => sequence.Id == year);
}
