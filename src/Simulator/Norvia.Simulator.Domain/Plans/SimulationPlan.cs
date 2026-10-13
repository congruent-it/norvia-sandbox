using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Plans;

/// <summary>What a run will send, made before anything is sent. The same seed always gives the same plan.</summary>
public sealed record SimulationPlan(int RunNumber, SimulationSettings Settings, IReadOnlyList<PlannedBooking> Bookings)
{
    /// <summary>How many bookings each Shipper sends, the busiest first.</summary>
    public IReadOnlyList<ShipperShare> PerShipper() =>
    [
        .. Bookings.GroupBy(booking => (booking.ShipperId, booking.ShipperName))
            .Select(group => new ShipperShare(group.Key.ShipperId, group.Key.ShipperName, group.Count(), group.Count(booking => booking.Bad is not null)))
            .OrderByDescending(share => share.Bookings)
            .ThenBy(share => share.ShipperId, StringComparer.Ordinal),
    ];
}

public sealed record ShipperShare(string ShipperId, string ShipperName, int Bookings, int BadBookings);
