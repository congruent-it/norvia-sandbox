using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Trips;

/// <summary>A Shipment going out on a Trip with the Transport Order details its Stops are derived from (ADR 0007).</summary>
public sealed record DispatchedShipment(Shipment Shipment, TransportOrderDetails Details);
