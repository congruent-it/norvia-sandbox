using Ardalis.SharedKernel;

namespace Norvia.Erp.Domain.CreditNotes;

/// <summary>
/// The gapless sequence of Credit note numbers of one calendar year, separate from the Shipper invoices'. As for invoices, a number is only
/// taken in the transaction that issues the Credit note: if it rolls back, so does the sequence. Two transactions that take the next number
/// at once conflict on the row version, so one of them takes the number after the other.
/// </summary>
public sealed class CreditNoteNumberSequence : EntityBase<int>, IAggregateRoot
{
    /// <summary>What six digits can number.</summary>
    public const int MaxPerYear = 999_999;

    private CreditNoteNumberSequence()
    {
    }

    /// <summary>Starts the sequence of a year, with no number taken yet.</summary>
    public CreditNoteNumberSequence(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        Id = year;
    }

    /// <summary>The calendar year the sequence numbers, which is also its key.</summary>
    public int Year => Id;

    /// <summary>The last number taken; 0 before the first.</summary>
    public int LastTaken { get; private set; }

    public CreditNoteNumber TakeNext()
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(LastTaken, MaxPerYear);
        LastTaken++;
        return CreditNoteNumber.Of(Year, LastTaken);
    }
}
