namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The demo's day of Shipments to put on Trips (P11), as the seed file gives it: Own fleet Transport Orders collected on one weekday in the
/// first week, which the seeder books, assigns to Own fleet and releases. The seed file gives the day relative to the day the demo starts;
/// this works out the real date and bookings.
/// </summary>
/// <param name="FirstDayFromStart">The earliest the day can be, in days after the demo starts. The day is the first weekday from then on.</param>
internal sealed record DemoTripDay(int FirstDayFromStart, string ShipperId, DemoCollection Collection, IReadOnlyList<DemoTransportOrder> TransportOrders)
{
    public DateOnly DayFor(DateOnly start) => DemoBookings.WeekdayOnOrAfter(start.AddDays(FirstDayFromStart));
}
