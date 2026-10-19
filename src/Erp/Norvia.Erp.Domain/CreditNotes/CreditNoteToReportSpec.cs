using Ardalis.Specification;

namespace Norvia.Erp.Domain.CreditNotes;

/// <summary>One Credit note, tracked, for the Shipper invoice Logic App's report on it and for Send again.</summary>
public sealed class CreditNoteToReportSpec : Specification<CreditNote>
{
    public CreditNoteToReportSpec(CreditNoteNumber number) => Query.Where(note => note.Id == number);
}
