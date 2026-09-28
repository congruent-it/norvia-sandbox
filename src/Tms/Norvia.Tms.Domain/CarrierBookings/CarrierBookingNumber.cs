using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// The number the TMS gives a Carrier booking when it creates it at release, such as <c>CB-0000042</c>. It links the
/// release to the Carrier's answer.
/// </summary>
public sealed partial record CarrierBookingNumber
{
    private const long MaxSequence = 9_999_999;

    private CarrierBookingNumber(string value) => Value = value;

    public string Value { get; }

    public static CarrierBookingNumber FromSequence(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaxSequence);
        return new CarrierBookingNumber(string.Create(CultureInfo.InvariantCulture, $"CB-{sequence:D7}"));
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out CarrierBookingNumber? number)
    {
        number = value is not null && Format().IsMatch(value) ? new CarrierBookingNumber(value) : null;
        return number is not null;
    }

    public static CarrierBookingNumber Parse(string value) =>
        TryParse(value, out var number) ? number : throw new FormatException($"'{value}' is not a Carrier booking number.");

    public override string ToString() => Value;

    [GeneratedRegex("^CB-[0-9]{7}$")]
    private static partial Regex Format();
}
