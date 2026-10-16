namespace Norvia.PartnerPortal.Application.Reset;

/// <summary>
/// Whether a reset would be taken now: the setting is on. The Partner portal consumes nothing from Service Bus, so it has no subscription to wait for
/// and <paramref name="BusySubscription"/> is always null; the answer has the same shape as the TMS API's and the Driver app API's.
/// </summary>
public sealed record ResetReadiness(bool Enabled, string? BusySubscription);
