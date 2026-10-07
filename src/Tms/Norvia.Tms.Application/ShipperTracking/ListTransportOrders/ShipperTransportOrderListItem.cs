using Norvia.Tms.Domain.ShipperTracking;

namespace Norvia.Tms.Application.ShipperTracking.ListTransportOrders;

/// <summary>
/// The Shipper's view of a Transport Order in a list (P8), the Canonical Model's shipper-transport-order-list-item. It shows no
/// Carrier, no Shipment number, no Exception and no Stale: they stay inside Norvia (ADR 0011).
/// </summary>
/// <param name="Eta">The ETA of the last Tracking event that carried one, otherwise the end of the delivery window; null when there is none.</param>
/// <param name="LastUpdate">The event time of the latest progress Tracking event; null before collection.</param>
public sealed record ShipperTransportOrderListItem(
    string TransportOrderNumber,
    string? ShipperReference,
    ShipperPlaceView? Collection,
    ShipperPlaceView? Delivery,
    TrackingStatus TrackingStatus,
    DateTimeOffset? Eta,
    DateTimeOffset? LastUpdate);

public sealed record ShipperPlaceView(string? City, string? CountryCode);
