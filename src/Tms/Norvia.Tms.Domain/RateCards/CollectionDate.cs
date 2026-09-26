using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>The day a Rate Card version must be valid on to rate a Transport Order.</summary>
internal static class CollectionDate
{
    private static readonly TimeZoneInfo Norvia = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>The date of the earliest collection time, in Norvia's time zone; null when the order has none.</summary>
    public static DateOnly? Of(TransportOrderDetails details) =>
        details.Collection?.Window?.Earliest is { } earliest
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(earliest, Norvia).DateTime)
            : null;
}
