namespace Norvia.DriverApp.Domain.Trips;

/// <summary>Which Stop of a Trip: the Shipment, and its collection or its delivery. The two always travel together.</summary>
public readonly record struct StopRef(string ShipmentNumber, StopKind Kind);
