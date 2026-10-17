using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Browse;

/// <summary>An Accepted or Planned Transport Order in the TMS's list, with its Shipment once it has one.</summary>
/// <param name="CollectionPlace">Where the goods are collected; null when the Shipper gave no address.</param>
/// <param name="DeliveryPlace">Where the goods are delivered; null when the Shipper gave no address.</param>
/// <param name="ShipmentNumber">The Transport Order's one Shipment; null until it is Planned.</param>
/// <param name="ShipmentStatus">The Shipment's status; null until the Transport Order is Planned.</param>
public sealed record TransportOrderListItem(
    string TransportOrderNumber,
    TransportOrderStatus Status,
    ShipperView Shipper,
    string? ShipperReference,
    PlaceView? CollectionPlace,
    PlaceView? DeliveryPlace,
    DateTimeOffset? CollectionEarliest,
    DateTimeOffset? CollectionLatest,
    string? ShipmentNumber,
    ShipmentStatus? ShipmentStatus);
