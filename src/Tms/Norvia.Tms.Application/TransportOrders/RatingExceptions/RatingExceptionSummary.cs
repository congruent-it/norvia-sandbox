namespace Norvia.Tms.Application.TransportOrders.RatingExceptions;

/// <summary>A Transport Order whose Rating failed, as Customer service follows it up and re-rates it.</summary>
/// <param name="RatingFailedAt">When Rating last failed: a re-rate that fails again moves it on.</param>
public sealed record RatingExceptionSummary(
    Guid Id,
    string TransportOrderNumber,
    ShipperView Shipper,
    string? ShipperReference,
    DateTimeOffset? CollectionEarliest,
    DateTimeOffset ReceivedAt,
    RatingFailureView RatingFailure,
    DateTimeOffset RatingFailedAt);
