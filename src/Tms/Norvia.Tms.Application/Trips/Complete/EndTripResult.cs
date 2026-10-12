namespace Norvia.Tms.Application.Trips.Complete;

/// <param name="Version">The Trip's version when it ended. Ending doesn't change it.</param>
/// <param name="CorrelationId">The Driver's action trail, which <c>TripCompleted</c> carries.</param>
/// <param name="Delivered">The numbers of the Shipments delivered.</param>
/// <param name="NotDelivered">The numbers of the Shipments collected but not delivered, now Awaiting re-delivery.</param>
/// <param name="NotCollected">The numbers of the Shipments not collected, now Planned again.</param>
public sealed record EndTripResult(
    string Number,
    int Version,
    string CorrelationId,
    IReadOnlyList<string> Delivered,
    IReadOnlyList<string> NotDelivered,
    IReadOnlyList<string> NotCollected);
