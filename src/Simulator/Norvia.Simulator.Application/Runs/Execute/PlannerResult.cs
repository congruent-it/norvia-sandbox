namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the Planner's stage came to: whether the run was cancelled, the Shipments it left planned onto a Carrier, and those it left planned onto the Own fleet, in plan order.</summary>
internal sealed record PlannerResult(bool Stopped, IReadOnlyList<PlannedShipment> CarrierShipments, IReadOnlyList<OwnFleetShipment> OwnFleet);
