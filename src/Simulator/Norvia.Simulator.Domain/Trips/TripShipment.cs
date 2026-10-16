using Norvia.Simulator.Domain.Planner;

namespace Norvia.Simulator.Domain.Trips;

/// <summary>An Own fleet Shipment, Released, that waits for a Trip on its collection day: what it weighs and takes up, and when it collects.</summary>
public sealed record TripShipment(string ShipmentNumber, FleetLoad Load, DateTime CollectionFrom);
