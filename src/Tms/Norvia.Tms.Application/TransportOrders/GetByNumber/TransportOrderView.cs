using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.GetByNumber;

public sealed record TransportOrderView(
    string TransportOrderNumber,
    TransportOrderStatus Status,
    ShipperView Shipper,
    string CorrelationId,
    DateTimeOffset ReceivedAt,
    CanonicalTransportOrder TransportOrder);
