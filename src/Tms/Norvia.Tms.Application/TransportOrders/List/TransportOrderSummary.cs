using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.List;

public sealed record TransportOrderSummary(
    string TransportOrderNumber,
    TransportOrderStatus Status,
    ShipperView Shipper,
    string? ShipperReference,
    DateTimeOffset? CollectionEarliest,
    DateTimeOffset? CollectionLatest,
    DateTimeOffset ReceivedAt);
