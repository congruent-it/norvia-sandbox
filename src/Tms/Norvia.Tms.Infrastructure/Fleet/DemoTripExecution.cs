using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Infrastructure.Fleet;

/// <summary>
/// The Trip of P12's demo, as the seed file gives it: Own fleet Transport Orders collected on one day, put on a Trip for a seeded Driver and Vehicle
/// and Dispatched, so the Driver app holds it. The seed file gives the day relative to the day the demo starts; this works out the real date and bookings.
/// </summary>
/// <param name="FirstDayFromStart">The earliest the day can be, in days after the demo starts: 0 is today. The day is the first weekday from then on.</param>
/// <param name="PlannedStart">The Trip's planned start, within the Driver's Shift, as HH:mm.</param>
internal sealed record DemoTripExecution(
    int FirstDayFromStart,
    string ShipperId,
    string DriverId,
    string VehicleId,
    string PlannedStart,
    int PlannedDrivingMinutes,
    DemoCollection Collection,
    IReadOnlyList<DemoTransportOrder> TransportOrders)
{
    /// <summary>The first weekday on or after <see cref="FirstDayFromStart"/> days from the day the demo starts, so the Driver is rostered to work that day.</summary>
    public DateOnly DayFor(DateOnly start) => DemoBookings.WeekdayOnOrAfter(start.AddDays(FirstDayFromStart));

    public DriverId Driver => new(DriverId);

    public VehicleId Vehicle => new(VehicleId);

    public TimeOnly Start => TimeOnly.ParseExact(PlannedStart, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public TimeSpan DrivingDuration => TimeSpan.FromMinutes(PlannedDrivingMinutes);
}
