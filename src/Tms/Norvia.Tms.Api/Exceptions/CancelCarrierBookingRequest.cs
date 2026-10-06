namespace Norvia.Tms.Api.Exceptions;

/// <summary>Why the Planner cancels the Confirmed Carrier booking.</summary>
/// <param name="Reason">Required: why Norvia cancels it, which the Carrier is told and the Planner sees in Shipments to release.</param>
public sealed record CancelCarrierBookingRequest(string? Reason);
