using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.TransportOrders;

/// <summary>The response to a booking the TMS accepted.</summary>
public sealed record AcceptedTransportOrder(string TransportOrderNumber, TransportOrderStatus Status);
