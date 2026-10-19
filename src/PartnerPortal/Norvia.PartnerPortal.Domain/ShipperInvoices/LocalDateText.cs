using System.Globalization;

namespace Norvia.PartnerPortal.Domain.ShipperInvoices;

/// <summary>A local date as the Shipper invoice format writes it: <c>dd-MM-yyyy</c>.</summary>
public static class LocalDateText
{
    public const string Format = "dd-MM-yyyy";

    public static string Of(DateOnly value) => value.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>The date the text says, or false for any other text.</summary>
    public static bool TryParse(string? text, out DateOnly value) =>
        DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
