namespace Norvia.DriverApp.Domain.Trips;

/// <summary>A Trip's number, such as <c>TR-0000007</c>, which stays the same through every version.</summary>
public readonly record struct TripNumber(string Value) : IComparable<TripNumber>, IComparable
{
    /// <summary>In the order of the numbers, so a query that orders Trips can run in memory as well as in the database.</summary>
    public int CompareTo(TripNumber other) => string.CompareOrdinal(Value, other.Value);

    int IComparable.CompareTo(object? obj) =>
        obj is TripNumber other ? CompareTo(other) : throw new ArgumentException($"Not a {nameof(TripNumber)}.", nameof(obj));

    public static bool operator <(TripNumber left, TripNumber right) => left.CompareTo(right) < 0;

    public static bool operator <=(TripNumber left, TripNumber right) => left.CompareTo(right) <= 0;

    public static bool operator >(TripNumber left, TripNumber right) => left.CompareTo(right) > 0;

    public static bool operator >=(TripNumber left, TripNumber right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value;
}
