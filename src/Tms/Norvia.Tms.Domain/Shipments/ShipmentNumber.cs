using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// The number the TMS gives a Shipment when it is planned, such as <c>SH-0000042</c>. It stays the Shipment's for life.
/// </summary>
public sealed partial record ShipmentNumber
{
    private const long MaxSequence = 9_999_999;

    private ShipmentNumber(string value) => Value = value;

    public string Value { get; }

    public static ShipmentNumber FromSequence(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaxSequence);
        return new ShipmentNumber(string.Create(CultureInfo.InvariantCulture, $"SH-{sequence:D7}"));
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out ShipmentNumber? number)
    {
        number = value is not null && Format().IsMatch(value) ? new ShipmentNumber(value) : null;
        return number is not null;
    }

    public static ShipmentNumber Parse(string value) =>
        TryParse(value, out var number) ? number : throw new FormatException($"'{value}' is not a Shipment number.");

    public override string ToString() => Value;

    [GeneratedRegex("^SH-[0-9]{7}$")]
    private static partial Regex Format();
}
