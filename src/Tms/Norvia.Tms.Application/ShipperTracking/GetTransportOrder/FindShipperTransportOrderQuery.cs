using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ShipperTracking.GetTransportOrder;

/// <param name="ShipperId">The Shipper whose Transport Order is asked for, from the caller's header: another Shipper's is not found.</param>
/// <param name="ShipperReference">The Shipper's own reference, unique per Shipper.</param>
public sealed record FindShipperTransportOrderQuery(string ShipperId, string ShipperReference) : IQuery<Result<ShipperTransportOrderDetail>>;
