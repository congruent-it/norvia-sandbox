namespace Norvia.Tms.Application.Trips.Start;

/// <param name="Version">The Trip's version when it started. Starting doesn't change it.</param>
/// <param name="CorrelationId">The Driver's action trail, which <c>TripStarted</c> carries.</param>
/// <param name="Repeat">True when the Driver app event had started the Trip already: the call changed nothing and published nothing.</param>
public sealed record StartTripResult(string Number, int Version, string CorrelationId, bool Repeat = false);
