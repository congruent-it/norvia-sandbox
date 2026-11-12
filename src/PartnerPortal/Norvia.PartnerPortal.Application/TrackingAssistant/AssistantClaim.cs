namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>A claim of the answer and where in the tracking data it came from.</summary>
/// <param name="Field">A path into the tracking data, such as <c>detail.eta</c> or <c>orders[2].status</c>.</param>
public sealed record AssistantClaim(string Field);
