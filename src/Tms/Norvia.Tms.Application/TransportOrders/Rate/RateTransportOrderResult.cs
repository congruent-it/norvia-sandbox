using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// The rating result a Transport Order has after a rating, and why, when Rating failed. It names the Shipper and the
/// intake's correlation ID, as a rating is traced on its intake's Message trail.
/// </summary>
/// <param name="RatingFailure">Null unless the rating result is Rating failed.</param>
/// <param name="Repeat">True when a re-rate was sent again under its key, and answered as before without rating.</param>
public sealed record RateTransportOrderResult(
    RatingResult RatingResult,
    RatingFailureView? RatingFailure,
    string ShipperId,
    string CorrelationId,
    bool Repeat = false);
