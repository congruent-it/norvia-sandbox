using System.Globalization;

namespace Norvia.Erp.Domain.CreditNotes;

/// <summary>A Credit note's number, <c>CN-&lt;year&gt;-&lt;6 digits&gt;</c>, from its own gapless sequence per calendar year.</summary>
public readonly record struct CreditNoteNumber : IComparable<CreditNoteNumber>, IComparable
{
    public CreditNoteNumber(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static CreditNoteNumber Of(int year, int sequence) =>
        new(string.Create(CultureInfo.InvariantCulture, $"CN-{year:D4}-{sequence:D6}"));

    public static bool operator <(CreditNoteNumber left, CreditNoteNumber right) => left.CompareTo(right) < 0;

    public static bool operator <=(CreditNoteNumber left, CreditNoteNumber right) => left.CompareTo(right) <= 0;

    public static bool operator >(CreditNoteNumber left, CreditNoteNumber right) => left.CompareTo(right) > 0;

    public static bool operator >=(CreditNoteNumber left, CreditNoteNumber right) => left.CompareTo(right) >= 0;

    public int CompareTo(CreditNoteNumber other) => string.CompareOrdinal(Value, other.Value);

    public int CompareTo(object? obj) => obj is CreditNoteNumber other ? CompareTo(other) : 1;

    public override string ToString() => Value;
}
