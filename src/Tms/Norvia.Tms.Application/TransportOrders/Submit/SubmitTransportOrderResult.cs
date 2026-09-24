using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

public sealed record SubmitTransportOrderResult(string TransportOrderNumber, TransportOrderStatus Status);
