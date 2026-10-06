using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;

/// <summary>
/// From an open Exception the Planner sets the delivery window they agreed with the Carrier outside the TMS on a Collected
/// or In transit Shipment (P7). The action starts in the TMS UI, so its correlation ID is the TMS's own. The agreement sent
/// again under its key is a repeat, answered as before.
/// </summary>
/// <param name="AgreementKey">The idempotency key that makes a retry of the agreement a repeat.</param>
public sealed record AgreeDeliveryWindowCommand(
    string ExceptionNumber, DateTimeOffset Earliest, DateTimeOffset Latest, string AgreementKey, string CorrelationId)
    : ICommand<Result<AgreeDeliveryWindowResult>>;
