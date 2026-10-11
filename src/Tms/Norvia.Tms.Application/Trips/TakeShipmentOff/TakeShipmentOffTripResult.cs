namespace Norvia.Tms.Application.Trips.TakeShipmentOff;

/// <param name="Version">The Trip's new version, or null when the Trip is a Draft, which has no version to send.</param>
/// <param name="CorrelationId">The correlation ID <c>ShipmentTakenOffTrip</c> was published under, and the new version too.</param>
public sealed record TakeShipmentOffTripResult(string TripNumber, string ShipmentNumber, int? Version, string CorrelationId);
