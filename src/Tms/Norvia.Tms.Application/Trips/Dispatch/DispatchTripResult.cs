namespace Norvia.Tms.Application.Trips.Dispatch;

/// <param name="Version">The version of the Trip that was sent: 1.</param>
/// <param name="CorrelationId">The Dispatch's Message trail, which <c>TripDispatched</c> carries.</param>
public sealed record DispatchTripResult(string Number, int Version, string CorrelationId);
