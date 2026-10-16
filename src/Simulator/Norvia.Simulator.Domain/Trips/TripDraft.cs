using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.Trips;

/// <summary>A Trip the Planner means to create for a day: its Vehicle and Driver, planned start inside the Driver's Shift, planned driving duration and the Shipments on it, in the order of their collection.</summary>
public sealed record TripDraft(
    DateOnly Day, SimulatedVehicle Vehicle, SimulatedDriver Driver, TimeOnly PlannedStart, TimeSpan PlannedDrivingDuration, IReadOnlyList<TripShipment> Shipments);
