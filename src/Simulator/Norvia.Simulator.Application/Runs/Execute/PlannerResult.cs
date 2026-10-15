namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the Planner's stage came to: whether the run was cancelled, and the Shipments it left planned onto a Carrier, in plan order.</summary>
internal sealed record PlannerResult(bool Stopped, IReadOnlyList<PlannedShipment> CarrierShipments);
