using Ardalis.Specification;

namespace Norvia.Erp.Domain.CreditNotes;

/// <summary>The Credit note number sequence of one calendar year, tracked: the issuing transaction takes the next number from it.</summary>
public sealed class CreditNoteNumberSequenceOfYearSpec : Specification<CreditNoteNumberSequence>
{
    public CreditNoteNumberSequenceOfYearSpec(int year) => Query.Where(sequence => sequence.Id == year);
}
