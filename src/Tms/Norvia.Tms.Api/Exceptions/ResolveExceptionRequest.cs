namespace Norvia.Tms.Api.Exceptions;

/// <summary>How the Planner resolves an Exception: one of the outcomes, and the remark every outcome needs.</summary>
/// <param name="Outcome"><c>NoActionNeeded</c>, <c>Redelivered</c>, <c>Replanned</c>, <c>ReturnedToShipper</c> or <c>ClaimHandledOutsideTms</c>.</param>
/// <param name="Remark">Required: what the Planner did or concluded.</param>
public sealed record ResolveExceptionRequest(string? Outcome, string? Remark);
