using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Plans;

/// <summary>
/// What a run will send, made before anything is sent: the bookings, and what the Planner means to do with the Transport Orders
/// they become. The same seed always gives the same plan. A plan made before the Planner existed has no choices.
/// </summary>
public sealed record SimulationPlan(int RunNumber, SimulationSettings Settings, IReadOnlyList<PlannedBooking> Bookings)
{
    /// <summary>The Planner's choices, one per Transport Order it will assign, by the booking's step.</summary>
    public IReadOnlyList<PlannerChoice> Choices { get; init; } = [];

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
