namespace Norvia.Tms.Api.Exceptions;

/// <summary>The delivery window the Planner agreed with the Carrier outside the TMS.</summary>
/// <param name="Earliest">Required: the window's earliest time.</param>
/// <param name="Latest">Required: the window's latest time, after now and not before the earliest.</param>
public sealed record AgreeDeliveryWindowRequest(DateTimeOffset? Earliest, DateTimeOffset? Latest);
