using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The number the TMS assigns to a Transport Order on acceptance, such as <c>TO-0000042</c>.
/// </summary>
public sealed partial record TransportOrderNumber
{
    private const long MaxSequence = 9_999_999;

    private TransportOrderNumber(string value) => Value = value;

    public string Value { get; }

    public static TransportOrderNumber FromSequence(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaxSequence);
        return new TransportOrderNumber(string.Create(CultureInfo.InvariantCulture, $"TO-{sequence:D7}"));
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out TransportOrderNumber? number)
    {
        number = value is not null && Format().IsMatch(value) ? new TransportOrderNumber(value) : null;
        return number is not null;
    }

    public static TransportOrderNumber Parse(string value) =>
        TryParse(value, out var number) ? number : throw new FormatException($"'{value}' is not a Transport Order number.");

    public override string ToString() => Value;

    [GeneratedRegex("^TO-[0-9]{7}$")]
    private static partial Regex Format();
}
