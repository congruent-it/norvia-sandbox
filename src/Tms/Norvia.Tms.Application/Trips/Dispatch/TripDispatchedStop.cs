using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Dispatch;

/// <param name="Sequence">The Stop's place in the sequence, counting from 1.</param>
/// <param name="Kind"><c>Collection</c> or <c>Delivery</c>.</param>
/// <param name="Window">The collection window, or for a delivery the last agreed one, otherwise the Shipper's.</param>
/// <param name="Consignee">Who receives the goods at a delivery Stop; null at a collection Stop.</param>
public sealed record TripDispatchedStop(
    int Sequence,
    string Kind,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    CanonicalAddress? Address,
    CanonicalTimeWindow? Window,
    IReadOnlyList<CanonicalGoodsLine> GoodsLines,
    CanonicalConsignee? Consignee)
{
    public static TripDispatchedStop From(TripStop stop, DispatchedShipment onTrip)
    {
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentNullException.ThrowIfNull(onTrip);
        var (shipment, details) = (onTrip.Shipment, onTrip.Details);
        var collects = stop.Kind == StopKind.Collection;
        return new(
            stop.Position + 1,
            stop.Kind.ToString(),
            shipment.Number.Value,
            shipment.TransportOrderNumber.Value,
            shipment.ShipperId.Value,
            CanonicalAddress.From(collects ? details.Collection?.Address : details.Delivery?.Address),
            CanonicalTimeWindow.From(collects ? details.Collection?.Window : shipment.DeliveryWindowOf(details)),
            [.. details.GoodsLines.Select(CanonicalGoodsLine.From)],
            collects ? null : CanonicalConsignee.From(details.Delivery?.Consignee));
    }
}
