namespace Norvia.Tms.Api.Trips;

/// <param name="ShipmentNumbers">The Shipments to put on the Trip, such as ["SH-0000012"]. A Shipment already on the Trip stays where it is.</param>
public sealed record AddShipmentsRequest(IReadOnlyList<string>? ShipmentNumbers);
