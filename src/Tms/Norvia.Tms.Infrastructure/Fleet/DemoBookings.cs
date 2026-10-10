using System.Globalization;
using Norvia.Tms.Application.CanonicalModel;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>What the demo's days share: how a day is found from the start and how a seeded Transport Order is booked for it.</summary>
internal static class DemoBookings
{
    /// <summary>The day itself, or the next weekday when it falls on a weekend, so Drivers are rostered to work.</summary>
    public static DateOnly WeekdayOnOrAfter(DateOnly day)
    {
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    /// <summary>The booking for one seeded Transport Order, collected on <paramref name="day"/> in <paramref name="zone"/>. Its Shipper reference carries the day, so a demo started on another day books its own.</summary>
    public static CanonicalTransportOrder For(string shipperId, DemoCollection collection, DateOnly day, DemoTransportOrder order, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(zone);
        var delivery = order.Delivery;
        return new CanonicalTransportOrder(
            null,
            shipperId,
            string.Create(CultureInfo.InvariantCulture, $"{order.ShipperReference}-{day:yyyyMMdd}"),
            new CanonicalCollectionDetails(
                new CanonicalAddress(collection.Street, collection.Postcode, collection.City, collection.Country),
                new CanonicalTimeWindow(At(day, collection.Earliest, zone), At(day, collection.Latest, zone))),
            new CanonicalDeliveryDetails(
                new CanonicalAddress(delivery.Street, delivery.Postcode, delivery.City, delivery.Country),
                new CanonicalConsignee(delivery.Consignee.Name, delivery.Consignee.ContactName, delivery.Consignee.Phone, null),
                null),
            [new CanonicalGoodsLine("pallet", order.Pallets, order.Pallets * order.WeightKgPerPallet, null)],
            null);
    }

    private static DateTimeOffset At(DateOnly day, string time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture));
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
