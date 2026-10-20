namespace Norvia.Erp.Application.Reset;

/// <summary>Whether a reset would be taken now: the setting is on, and no relevant Service Bus subscription holds unprocessed messages.</summary>
/// <param name="Enabled">Whether the estate switched the reset on for this application.</param>
/// <param name="BusySubscription">The first subscription holding unprocessed messages, as <c>topic/subscription</c>, or null when all are quiet.</param>
public sealed record ResetReadiness(bool Enabled, string? BusySubscription);
