using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.Billing.Release;

namespace Norvia.Tms.Application.Billing.GetReleaseEligibility;

/// <summary>Whether a Transport Order may be released without a Proof of delivery, and which other conditions still block it (P9).</summary>
public sealed record GetReleaseEligibilityQuery(string TransportOrderNumber) : IQuery<Result<ReleaseEligibility>>;
