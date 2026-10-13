using Mediator;

namespace Norvia.DriverApp.Application.Trips.GetStop;

/// <summary>The Stop screen: one Stop of one of the Driver's Trips. Null when the Driver has no such Stop.</summary>
public sealed record GetDriverStopQuery(string DriverId, string TripNumber, string ShipmentNumber, StopKindDto Kind) : IQuery<DriverStopView?>;
