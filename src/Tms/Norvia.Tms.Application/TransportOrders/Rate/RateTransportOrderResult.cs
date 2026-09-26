using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>The rating result a Transport Order has after a rating, and why, when Rating failed.</summary>
/// <param name="RatingFailure">Null unless the rating result is Rating failed.</param>
public sealed record RateTransportOrderResult(RatingResult RatingResult, RatingFailureView? RatingFailure);
