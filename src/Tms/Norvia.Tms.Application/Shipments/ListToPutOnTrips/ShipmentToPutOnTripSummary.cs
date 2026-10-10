using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Application.Shipments.ListToPutOnTrips;

/// <summary>A Shipment in the Planner's list of Shipments to put on Trips: where and when it collects and delivers, and what it asks of a Vehicle.</summary>
public sealed record ShipmentToPutOnTripSummary(
    string ShipmentNumber,
    string TransportOrderNumber,
    ShipperView Shipper,
    DateOnly? CollectionDay,
    string CollectionPlace,
    DateTimeOffset? CollectionEarliest,
    DateTimeOffset? CollectionLatest,
    string DeliveryPlace,
    DateTimeOffset? DeliveryEarliest,
    DateTimeOffset? DeliveryLatest,
    decimal WeightKg,
    int PalletPlaces);
