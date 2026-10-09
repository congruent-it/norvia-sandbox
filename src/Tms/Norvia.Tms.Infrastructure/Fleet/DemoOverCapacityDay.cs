using System.Globalization;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Infrastructure.RateCards;
using Norvia.Tms.Infrastructure.Shippers;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The demo's Over capacity day (P10), as the seed file gives it: a rigid truck Off the road and a CE Driver on leave on one
/// weekday in the first week, and the Own fleet Transport Orders that fill the rest of that day's capacity. The seed file
/// gives it relative to the day the demo starts; this works out the real dates and bookings.
/// </summary>
/// <param name="FirstDayFromStart">The earliest the day can be, in days after the demo starts. The day is the first weekday from then on.</param>
internal sealed record DemoOverCapacityDay(
    int FirstDayFromStart,
    DemoOffTheRoad OffTheRoad,
    DemoAbsence Absence,
    string ShipperId,
    DemoCollection Collection,
    IReadOnlyList<DemoTransportOrder> TransportOrders)
{
    /// <summary>The first weekday on or after <see cref="FirstDayFromStart"/> days from the day the demo starts, so Drivers are rostered to work that day.</summary>
    public DateOnly DayFor(DateOnly start)
    {
        var day = start.AddDays(FirstDayFromStart);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    /// <summary>The booking for one of the demo's Transport Orders, collected on <paramref name="day"/> in <paramref name="zone"/>. Its Shipper reference carries the day, so a demo started on another day books its own.</summary>
    public CanonicalTransportOrder BookingFor(DateOnly day, DemoTransportOrder order, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(zone);
        var delivery = order.Delivery;
        return new CanonicalTransportOrder(
            null,
            ShipperId,
            string.Create(CultureInfo.InvariantCulture, $"{order.ShipperReference}-{day:yyyyMMdd}"),
            new CanonicalCollectionDetails(
                new CanonicalAddress(Collection.Street, Collection.Postcode, Collection.City, Collection.Country),
                new CanonicalTimeWindow(At(day, Collection.Earliest, zone), At(day, Collection.Latest, zone))),
            new CanonicalDeliveryDetails(
                new CanonicalAddress(delivery.Street, delivery.Postcode, delivery.City, delivery.Country),
                new CanonicalConsignee(delivery.Consignee.Name, delivery.Consignee.ContactName, delivery.Consignee.Phone, null),
                null),
            [new CanonicalGoodsLine("pallet", order.Pallets, order.Pallets * order.WeightKgPerPallet, null)],
            null);
    }

    public VehicleId VehicleId => new(OffTheRoad.VehicleId);

    public DriverId DriverId => new(Absence.DriverId);

    private static DateTimeOffset At(DateOnly day, string time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture));
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}

internal sealed record DemoOffTheRoad(string VehicleId, string Reason, string? Remark);

internal sealed record DemoAbsence(string DriverId, string Reason, string? Remark);

internal sealed record DemoCollection(string Street, string Postcode, string City, string Country, string Earliest, string Latest);

internal sealed record DemoDelivery(string Street, string Postcode, string City, string Country, DemoConsignee Consignee);

internal sealed record DemoConsignee(string Name, string ContactName, string Phone);

internal sealed record DemoTransportOrder(string ShipperReference, DemoDelivery Delivery, int Pallets, decimal WeightKgPerPallet);

/// <summary>What the seed file holds for the demo's Over capacity day: nothing when it has no such part.</summary>
internal sealed record DemoSeed(DemoOverCapacityDay? OverCapacityDay)
{
    public static DemoSeed Read(IOptions<SeedOptions> options) =>
        new(SeedFileReader.Read<SeedFile>(options).DemoOverCapacityDay);

    private sealed record SeedFile(DemoOverCapacityDay? DemoOverCapacityDay);
}
