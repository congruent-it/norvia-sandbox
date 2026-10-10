namespace Norvia.DriverApp.Domain.Trips;

public sealed record TripStop(
    int Sequence,
    StopKind Kind,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    StopAddress? Address,
    StopWindow? Window,
    IReadOnlyList<StopGoodsLine> GoodsLines,
    StopConsignee? Consignee);
