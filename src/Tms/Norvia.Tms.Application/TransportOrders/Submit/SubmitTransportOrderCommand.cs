using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.CanonicalModel;

namespace Norvia.Tms.Application.TransportOrders.Submit;

public sealed record SubmitTransportOrderCommand(CanonicalTransportOrder TransportOrder, string CorrelationId)
    : ICommand<Result<SubmitTransportOrderResult>>;
