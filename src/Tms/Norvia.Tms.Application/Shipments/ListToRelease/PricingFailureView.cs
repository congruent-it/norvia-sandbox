namespace Norvia.Tms.Application.Shipments.ListToRelease;

/// <summary>
/// Why a Carrier Shipment's Carrier Rate Card can't price it (P9): releasing it would fail with this code, and the Planner can
/// release it with a Spot price instead.
/// </summary>
/// <param name="Code">The release's refusal code: <c>NoRateCard</c>, <c>NoValidRateCardVersion</c>, <c>NoMatchingLane</c> or <c>WeightAboveHighestBreak</c>.</param>
/// <param name="Message">The readable reason, naming the Carrier.</param>
public sealed record PricingFailureView(string Code, string Message);
