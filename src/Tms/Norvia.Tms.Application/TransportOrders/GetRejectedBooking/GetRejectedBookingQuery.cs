using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TransportOrders.GetRejectedBooking;

/// <summary>Finds a Rejected booking by its ID, as Customer service opens one; it has no Transport Order number.</summary>
public sealed record GetRejectedBookingQuery(Guid Id) : IQuery<Result<TransportOrderView>>;
