using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The number the TMS gives a Trip when it is created, such as <c>TR-0000042</c>. It stays the Trip's for life.</summary>
public sealed partial record TripNumber
{
    private const long MaxSequence = 9_999_999;

    private TripNumber(string value) => Value = value;

    public string Value { get; }

    public static TripNumber FromSequence(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaxSequence);
        return new TripNumber(string.Create(CultureInfo.InvariantCulture, $"TR-{sequence:D7}"));
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out TripNumber? number)
    {
        number = value is not null && Format().IsMatch(value) ? new TripNumber(value) : null;
        return number is not null;
    }

    public static TripNumber Parse(string value) =>
        TryParse(value, out var number) ? number : throw new FormatException($"'{value}' is not a Trip number.");

    public override string ToString() => Value;

    [GeneratedRegex("^TR-[0-9]{7}$")]
    private static partial Regex Format();
}
