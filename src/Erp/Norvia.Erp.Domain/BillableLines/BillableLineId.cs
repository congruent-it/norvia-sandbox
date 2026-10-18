namespace Norvia.Erp.Domain.BillableLines;

/// <summary>A Billable line is identified by its Transport Order number, such as <c>TO-0000123</c>: there is one per Transport Order.</summary>
public readonly record struct BillableLineId : IComparable<BillableLineId>, IComparable
{
    public BillableLineId(string transportOrderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);
        TransportOrderNumber = transportOrderNumber;
    }

    public string TransportOrderNumber { get; }

    public static bool operator <(BillableLineId left, BillableLineId right) => left.CompareTo(right) < 0;

    public static bool operator <=(BillableLineId left, BillableLineId right) => left.CompareTo(right) <= 0;

    public static bool operator >(BillableLineId left, BillableLineId right) => left.CompareTo(right) > 0;

    public static bool operator >=(BillableLineId left, BillableLineId right) => left.CompareTo(right) >= 0;

    public int CompareTo(BillableLineId other) => string.CompareOrdinal(TransportOrderNumber, other.TransportOrderNumber);

    public int CompareTo(object? obj) => obj is BillableLineId other ? CompareTo(other) : 1;

    public override string ToString() => TransportOrderNumber;
}
