namespace Norvia.DriverApp.Application.Trips;

/// <summary>One Stop of a Trip version, as the Trip dispatch format has it and the Driver sees it.</summary>
public sealed record StopDto(
    int Sequence,
    StopKindDto Kind,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    StopAddressDto? Address,
    StopWindowDto? Window,
    IReadOnlyList<StopGoodsLineDto> GoodsLines,
    StopConsigneeDto? Consignee);
