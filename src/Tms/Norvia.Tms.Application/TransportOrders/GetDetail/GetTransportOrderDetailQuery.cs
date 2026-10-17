using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TransportOrders.GetDetail;

/// <summary>A Transport Order with its Shipment and the Trip the Shipment is on: what the TMS UI's detail page reads.</summary>
public sealed record GetTransportOrderDetailQuery(string TransportOrderNumber) : IQuery<Result<TransportOrderDetailView>>;
