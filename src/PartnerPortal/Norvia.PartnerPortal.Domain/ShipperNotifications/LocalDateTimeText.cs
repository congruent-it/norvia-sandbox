using System.Globalization;

namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

/// <summary>A local date and time as the Shipper notification format writes it: <c>dd-MM-yyyy HH:mm</c>, 24-hour clock, no offset.</summary>
public static class LocalDateTimeText
{
    public const string Format = "dd-MM-yyyy HH:mm";

    public static string Of(DateTime value) => value.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>The date and time the text says, or false for any other text.</summary>
    public static bool TryParse(string? text, out DateTime value) =>
        DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
