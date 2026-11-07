using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Billing.Release;

/// <summary>
/// Customer service releases a Delivered, Not yet billable Transport Order without a Proof of delivery (P9), with a
/// required remark. The action starts in the TMS UI, so its correlation ID is the TMS's own.
/// </summary>
/// <param name="ActingOn">The Agent run whose Suggestion the employee acted on, which the trace then names. Null when they acted on their own.</param>
public sealed record ReleaseWithoutProofOfDeliveryCommand(string TransportOrderNumber, string Remark, string ReleasedBy, string CorrelationId, AgentRun? ActingOn = null)
    : ICommand<Result<ReleaseWithoutProofOfDeliveryResult>>;
