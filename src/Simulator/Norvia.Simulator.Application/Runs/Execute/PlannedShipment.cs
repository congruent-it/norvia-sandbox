using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>A Shipment the Planner planned onto a Carrier, with the Transport Order it came from and the booking that made it.</summary>
internal sealed record PlannedShipment(PlannedBooking Booking, string TransportOrderNumber, string ShipmentNumber, SimulatedCarrier Carrier);
