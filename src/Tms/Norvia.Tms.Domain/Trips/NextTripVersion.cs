using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.Trips;

/// <summary>What a sent Trip's next version goes out with: its Vehicle and Driver, and the Shipments on it then, possibly none.</summary>
public sealed record NextTripVersion(Vehicle Vehicle, Driver Driver, IReadOnlyList<DispatchedShipment> Shipments);
