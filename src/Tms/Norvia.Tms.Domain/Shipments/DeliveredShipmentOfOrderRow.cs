using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>What the list of Transport Orders not yet billable needs of a Delivered Shipment (P9).</summary>
public sealed record DeliveredShipmentOfOrderRow(ShipmentId Id, ShipmentNumber Number, TransportOrderNumber TransportOrderNumber);
