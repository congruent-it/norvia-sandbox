using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TransportOrders.GetById;

/// <summary>Finds a Transport Order by its ID, which a Rejected booking has even though it has no number.</summary>
public sealed record GetTransportOrderByIdQuery(Guid Id) : IQuery<Result<TransportOrderView>>;
