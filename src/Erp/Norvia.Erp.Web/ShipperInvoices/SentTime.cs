using System.Globalization;

namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>How the lists show when a Shipper invoice was reported Sent: the time in Amsterdam, or a dash for an invoice that wasn't.</summary>
internal static class SentTime
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static string Format(DateTimeOffset? sentAt) =>
        sentAt is { } at
            ? TimeZoneInfo.ConvertTime(at, Amsterdam).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "—";
}
