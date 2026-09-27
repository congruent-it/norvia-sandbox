using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders;

/// <param name="TransportOrderNumber">Null for a Rejected booking.</param>
/// <param name="ShipmentNumber">The number of the Transport Order's one Shipment once it is Planned; null until then.</param>
/// <param name="RejectionReasons">Every rule a Rejected booking broke; empty otherwise.</param>
/// <param name="RatingResult">Null for a Rejected booking, which is never rated.</param>
/// <param name="FreightCharge">The frozen Freight Charge once the Transport Order is Rated; null until then.</param>
/// <param name="RatingFailure">Why the Rate Card couldn't price the Transport Order, once Rating failed; null otherwise.</param>
public sealed record TransportOrderView(
    Guid Id,
    string? TransportOrderNumber,
    TransportOrderStatus Status,
    string? ShipmentNumber,
    ShipperView Shipper,
    IntakeChannel Channel,
    string CorrelationId,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<RejectionReasonView> RejectionReasons,
    RatingResult? RatingResult,
    FreightChargeView? FreightCharge,
    RatingFailureView? RatingFailure,
    CanonicalTransportOrder TransportOrder)
{
    public static TransportOrderView Of(TransportOrder order, IShipperDirectory shippers)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(
            order.Id.Value,
            order.Number?.Value,
            order.Status,
            order.ShipmentNumber?.Value,
            ShipperView.Of(order.ShipperId, shippers),
            order.Channel,
            order.CorrelationId.Value,
            order.ReceivedAt,
            RejectionReasonView.From(order.RejectionReasons),
            order.RatingResult,
            FreightChargeView.From(order.FreightCharge),
            RatingFailureView.From(order.RatingFailure),
            CanonicalTransportOrder.From(order));
    }
}
