using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>The day a Transport Order collects: the day a Rate Card version must be valid on to rate it, and the day of the Own fleet's demand (P10).</summary>
public static class CollectionDate
{
    private static readonly TimeZoneInfo Norvia = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>The date of the earliest collection time, in Norvia's time zone; null when the order has none.</summary>
    public static DateOnly? Of(TransportOrderDetails details) =>
        details.Collection?.Window?.Earliest is { } earliest
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(earliest, Norvia).DateTime)
            : null;
}
