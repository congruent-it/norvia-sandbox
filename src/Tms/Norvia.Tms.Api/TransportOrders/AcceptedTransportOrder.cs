using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.TransportOrders;

/// <summary>The response to a booking the TMS accepted.</summary>
/// <param name="Repeat">True when the booking was submitted before: this is the original Transport Order.</param>
public sealed record AcceptedTransportOrder(string TransportOrderNumber, TransportOrderStatus Status, bool Repeat);
