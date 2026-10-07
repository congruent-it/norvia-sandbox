using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ShipperTracking.GetTransportOrder;

/// <param name="ShipperId">The Shipper whose Transport Order is asked for, from the caller's header: another Shipper's is not found.</param>
/// <param name="TransportOrderNumber">Norvia's number for the Transport Order.</param>
public sealed record GetShipperTransportOrderQuery(string ShipperId, string TransportOrderNumber) : IQuery<Result<ShipperTransportOrderDetail>>;
