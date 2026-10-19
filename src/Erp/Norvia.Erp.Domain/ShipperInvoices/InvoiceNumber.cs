using System.Globalization;

namespace Norvia.Erp.Domain.ShipperInvoices;

/// <summary>A Shipper invoice's number, <c>INV-&lt;year&gt;-&lt;6 digits&gt;</c>, from a gapless sequence per calendar year.</summary>
public readonly record struct InvoiceNumber : IComparable<InvoiceNumber>, IComparable
{
    public InvoiceNumber(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static InvoiceNumber Of(int year, int sequence) =>
        new(string.Create(CultureInfo.InvariantCulture, $"INV-{year:D4}-{sequence:D6}"));

    public static bool operator <(InvoiceNumber left, InvoiceNumber right) => left.CompareTo(right) < 0;

    public static bool operator <=(InvoiceNumber left, InvoiceNumber right) => left.CompareTo(right) <= 0;

    public static bool operator >(InvoiceNumber left, InvoiceNumber right) => left.CompareTo(right) > 0;

    public static bool operator >=(InvoiceNumber left, InvoiceNumber right) => left.CompareTo(right) >= 0;

    public int CompareTo(InvoiceNumber other) => string.CompareOrdinal(Value, other.Value);

    public int CompareTo(object? obj) => obj is InvoiceNumber other ? CompareTo(other) : 1;

    public override string ToString() => Value;
}
