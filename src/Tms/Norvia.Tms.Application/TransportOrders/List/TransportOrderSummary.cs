using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.List;

/// <param name="TransportOrderNumber">Null for a Rejected booking.</param>
/// <param name="RatingResult">Null for a Rejected booking, which is never rated.</param>
public sealed record TransportOrderSummary(
    Guid Id,
    string? TransportOrderNumber,
    TransportOrderStatus Status,
    RatingResult? RatingResult,
    ShipperView Shipper,
    string? ShipperReference,
    DateTimeOffset? CollectionEarliest,
    DateTimeOffset? CollectionLatest,
    DateTimeOffset ReceivedAt);
