using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>A Shipment the Planner planned onto the Own fleet, with the Transport Order it came from, the booking that made it and the day it collects.</summary>
internal sealed record OwnFleetShipment(PlannedBooking Booking, string TransportOrderNumber, string ShipmentNumber, DateTime CollectionFrom);
