using Norvia.Tms.Application.ShipperTracking.ListTransportOrders;
using Norvia.Tms.Domain.ShipperTracking;

namespace Norvia.Tms.Application.ShipperTracking.GetTransportOrder;

/// <summary>
/// The Shipper's view of one Transport Order (P8), the Canonical Model's shipper-transport-order-detail. It shows no Carrier, no
/// Shipment number, no Exception, no Stale and no remarks: they stay inside Norvia (ADR 0011).
/// </summary>
/// <param name="DeliveryWindow">The agreed delivery window when the Planner has set one (P7), otherwise the Transport Order's.</param>
/// <param name="Eta">The ETA of the last Tracking event that carried one, otherwise the end of the delivery window; null when there is none.</param>
/// <param name="LastUpdate">The event time of the latest progress Tracking event; null before collection.</param>
/// <param name="Progress">The progress Tracking events in event-time order.</param>
public sealed record ShipperTransportOrderDetail(
    string TransportOrderNumber,
    string? ShipperReference,
    ShipperPlaceView? Collection,
    ShipperPlaceView? Delivery,
    ShipperWindowView? CollectionWindow,
    ShipperWindowView? DeliveryWindow,
    TrackingStatus TrackingStatus,
    DateTimeOffset? Eta,
    DateTimeOffset? LastUpdate,
    IReadOnlyList<ShipperProgressEntry> Progress);

public sealed record ShipperWindowView(DateTimeOffset? Earliest, DateTimeOffset? Latest);

/// <param name="Type">Collected, Departed, Arrived, OutForDelivery or Delivered.</param>
public sealed record ShipperProgressEntry(string Type, DateTimeOffset EventTime, ShipperPlaceView Place);
