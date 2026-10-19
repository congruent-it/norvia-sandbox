using Ardalis.SharedKernel;

namespace Norvia.Erp.Domain.ShipperInvoices;

/// <summary>
/// The gapless sequence of Shipper invoice numbers of one calendar year, as Dutch invoicing rules require. A number is only taken in the
/// transaction that issues the invoice: if that transaction rolls back, so does the sequence, and the number is free again. Two
/// transactions that take the next number at once conflict on the row version, so one of them takes the number after the other.
/// </summary>
public sealed class InvoiceNumberSequence : EntityBase<int>, IAggregateRoot
{
    private InvoiceNumberSequence()
    {
    }

    /// <summary>Starts the sequence of a year, with no number taken yet.</summary>
    public InvoiceNumberSequence(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        Id = year;
    }

    /// <summary>The calendar year the sequence numbers, which is also its key.</summary>
    public int Year => Id;

    /// <summary>The last number taken; 0 before the first.</summary>
    public int LastTaken { get; private set; }

    public InvoiceNumber TakeNext()
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(LastTaken, MaxPerYear);
        LastTaken++;
        return InvoiceNumber.Of(Year, LastTaken);
    }

    /// <summary>What six digits can number.</summary>
    public const int MaxPerYear = 999_999;
}
