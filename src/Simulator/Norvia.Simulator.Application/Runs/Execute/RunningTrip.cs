using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>A Trip for today that the Planner dispatched, which its Driver now works: the Driver and the Shipments on it, in the order of their collection.</summary>
internal sealed record RunningTrip(string TripNumber, SimulatedDriver Driver, IReadOnlyList<OwnFleetShipment> Shipments);
