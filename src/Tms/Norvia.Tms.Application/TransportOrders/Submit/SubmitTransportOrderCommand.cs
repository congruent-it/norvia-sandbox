using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

/// <param name="Channel">The intake channel the Transport Order came from, recorded on it.</param>
public sealed record SubmitTransportOrderCommand(CanonicalTransportOrder TransportOrder, IntakeChannel Channel, string CorrelationId)
    : ICommand<Result<SubmitTransportOrderResult>>;
