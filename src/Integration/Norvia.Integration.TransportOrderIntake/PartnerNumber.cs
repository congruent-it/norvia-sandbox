using System.Globalization;

namespace Norvia.Integration.TransportOrderIntake;

/// <summary>A number in a partner's text: a decimal point or a decimal comma, and no thousands separators.</summary>
internal static class PartnerNumber
{
    /// <summary>The pattern of a number, for use inside a larger regular expression.</summary>
    public const string Pattern = @"\d+([.,]\d+)?";

    public static decimal Parse(string number) =>
        decimal.Parse(number.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    /// <summary>Drops trailing zeros that a unit conversion leaves, so 2.5 t reads as 2500 kg, not 2500.0.</summary>
    public static decimal Shortest(decimal number) => number / 1.0000000000000000000000000000m;
}
