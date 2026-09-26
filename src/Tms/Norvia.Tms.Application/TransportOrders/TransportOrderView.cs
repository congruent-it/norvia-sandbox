using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders;

/// <param name="TransportOrderNumber">Null for a Rejected booking.</param>
/// <param name="RejectionReasons">Every rule a Rejected booking broke; empty otherwise.</param>
/// <param name="RatingResult">Null for a Rejected booking, which is never rated.</param>
/// <param name="FreightCharge">The frozen Freight Charge once the Transport Order is Rated; null until then.</param>
public sealed record TransportOrderView(
    Guid Id,
    string? TransportOrderNumber,
    TransportOrderStatus Status,
    ShipperView Shipper,
    IntakeChannel Channel,
    string CorrelationId,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<RejectionReasonView> RejectionReasons,
    RatingResult? RatingResult,
    FreightChargeView? FreightCharge,
    CanonicalTransportOrder TransportOrder)
{
    public static TransportOrderView Of(TransportOrder order, IShipperDirectory shippers)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(
            order.Id.Value,
            order.Number?.Value,
            order.Status,
            ShipperView.Of(order.ShipperId, shippers),
            order.Channel,
            order.CorrelationId.Value,
            order.ReceivedAt,
            RejectionReasonView.From(order.RejectionReasons),
            order.RatingResult,
            FreightChargeView.From(order.FreightCharge),
            CanonicalTransportOrder.From(order));
    }
}
