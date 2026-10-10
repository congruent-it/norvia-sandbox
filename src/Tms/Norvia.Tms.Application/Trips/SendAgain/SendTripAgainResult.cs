namespace Norvia.Tms.Application.Trips.SendAgain;

/// <param name="Version">The version that was sent again: the same one.</param>
/// <param name="CorrelationId">The send-again's Message trail, which <c>TripDispatched</c> carries.</param>
public sealed record SendTripAgainResult(string Number, int Version, string CorrelationId);
