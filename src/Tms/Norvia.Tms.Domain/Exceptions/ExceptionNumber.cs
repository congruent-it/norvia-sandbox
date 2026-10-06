using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// The number the TMS gives an Exception when it opens it, such as <c>EX-0000042</c>. It stays the Exception's for life.
/// </summary>
public sealed partial record ExceptionNumber
{
    private const long MaxSequence = 9_999_999;

    private ExceptionNumber(string value) => Value = value;

    public string Value { get; }

    public static ExceptionNumber FromSequence(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaxSequence);
        return new ExceptionNumber(string.Create(CultureInfo.InvariantCulture, $"EX-{sequence:D7}"));
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out ExceptionNumber? number)
    {
        number = value is not null && Format().IsMatch(value) ? new ExceptionNumber(value) : null;
        return number is not null;
    }

    public static ExceptionNumber Parse(string value) =>
        TryParse(value, out var number) ? number : throw new FormatException($"'{value}' is not an Exception number.");

    public override string ToString() => Value;

    [GeneratedRegex("^EX-[0-9]{7}$")]
    private static partial Regex Format();
}
