using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>
/// What an Own fleet report (P12) says about where it belongs, in place of a Carrier booking reference: the Trip and the Shipment on it
/// the Driver reported, and the version of the Trip the Driver app held, which is kept for tracing only.
/// </summary>
/// <param name="DriverId">The Driver ID from the Driver's token (ADR 0005), never from the Driver app's payload.</param>
public sealed record OwnFleetReport(TripNumber TripNumber, DriverId DriverId, ShipmentNumber ShipmentNumber, int TripVersion);
