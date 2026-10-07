using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Billing.Release;

/// <summary>
/// Customer service releases a Delivered, Not yet billable Transport Order without a Proof of delivery (P9), with a
/// required remark. The action starts in the TMS UI, so its correlation ID is the TMS's own.
/// </summary>
public sealed record ReleaseWithoutProofOfDeliveryCommand(string TransportOrderNumber, string Remark, string ReleasedBy, string CorrelationId)
    : ICommand<Result<ReleaseWithoutProofOfDeliveryResult>>;
