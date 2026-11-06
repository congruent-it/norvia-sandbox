using Norvia.Simulator.Domain.Personas;
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

    /// <summary>
    /// The pools of realistic texts the Carriers draw from, kept with the plan so a recorded plan replays exactly with no model. A plan made
    /// before the personas existed has none, and its Carriers send the fixed words they always did.
    /// </summary>
    public IReadOnlyList<TextPool> Pools { get; init; } = [];

    /// <summary>The profile each Carrier writes in, by Carrier ID.</summary>
    public IReadOnlyDictionary<string, string> CarrierProfiles { get; init; } = new Dictionary<string, string>();

    /// <summary>The profile each Driver of the Own fleet writes in, by Driver ID.</summary>
    public IReadOnlyDictionary<string, string> DriverProfiles { get; init; } = new Dictionary<string, string>();

    /// <summary>What the operator should know about the pools, such as a model that failed and a built-in pool used instead.</summary>
    public IReadOnlyList<string> PoolNotes { get; init; } = [];

    /// <summary>The pool a Carrier draws from for a kind of text, or null when the plan has none for it.</summary>
    public TextPool? PoolFor(string carrierId, TextCategory category) =>
        CarrierProfiles.TryGetValue(carrierId, out var profile)
            ? Pools.FirstOrDefault(pool => pool.ProfileId == profile && pool.Category == category)
            : null;

    /// <summary>The pool a Driver draws from for a kind of text, or null when the plan has none for it.</summary>
    public TextPool? DriverPoolFor(string driverId, TextCategory category) =>
        DriverProfiles.TryGetValue(driverId, out var profile)
            ? Pools.FirstOrDefault(pool => pool.ProfileId == profile && pool.Category == category)
            : null;

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
