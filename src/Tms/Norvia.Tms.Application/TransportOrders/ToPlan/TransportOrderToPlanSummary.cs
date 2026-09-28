using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.ToPlan;

/// <summary>An Accepted Transport Order in the Planner's list of Transport Orders to plan.</summary>
/// <param name="CollectionPlace">Where the goods are collected; null when the Shipper gave no address.</param>
/// <param name="DeliveryPlace">Where the goods are delivered; null when the Shipper gave no address.</param>
public sealed record TransportOrderToPlanSummary(
    string TransportOrderNumber,
    RatingResult RatingResult,
    ShipperView Shipper,
    string? ShipperReference,
    PlaceView? CollectionPlace,
    PlaceView? DeliveryPlace,
    DateTimeOffset? CollectionEarliest,
    DateTimeOffset? CollectionLatest,
    DateTimeOffset ReceivedAt);
