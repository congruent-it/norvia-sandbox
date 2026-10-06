using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;

/// <summary>
/// From an open Exception the Planner sets the delivery window they agreed with the Carrier outside the TMS on a Collected
/// or In transit Shipment (P7). The action starts in the TMS UI, so its correlation ID is the TMS's own.
/// </summary>
public sealed record AgreeDeliveryWindowCommand(string ExceptionNumber, DateTimeOffset Earliest, DateTimeOffset Latest, string CorrelationId)
    : ICommand<Result<AgreeDeliveryWindowResult>>;
