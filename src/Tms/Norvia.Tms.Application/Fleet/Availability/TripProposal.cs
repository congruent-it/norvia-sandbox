using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Availability;

/// <summary>
/// What P11 asks P10 about: a Trip as it stands, or as the Planner wants it to become. A Draft may lack a Vehicle, a Driver
/// or a driving duration, and the rules about what is missing don't apply.
/// </summary>
/// <param name="Day">The Trip's day.</param>
/// <param name="Load">The total weight and pallets of the Trip's Shipments.</param>
/// <param name="DrivingDuration">The planned driving duration the Planner entered.</param>
/// <param name="VehicleHasOtherTrip">True when the Vehicle has another Trip that day. P11 owns Trips, so it says so.</param>
/// <param name="DriverHasOtherTrip">True when the Driver has another Trip that day.</param>
public sealed record TripProposal(
    DateOnly Day,
    FleetLoad Load,
    VehicleId? Vehicle = null,
    DriverId? Driver = null,
    TimeSpan? DrivingDuration = null,
    bool VehicleHasOtherTrip = false,
    bool DriverHasOtherTrip = false);
