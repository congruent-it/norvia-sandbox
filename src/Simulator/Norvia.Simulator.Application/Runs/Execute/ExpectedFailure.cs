namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>A Shipment a Driver reported a failure, damage or reservations on, and the kind of Exception the TMS is expected to open for it.</summary>
internal sealed record ExpectedFailure(OwnFleetShipment Shipment, string Kind);
