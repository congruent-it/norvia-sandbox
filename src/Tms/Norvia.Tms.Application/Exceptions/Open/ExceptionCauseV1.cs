namespace Norvia.Tms.Application.Exceptions.Open;

/// <summary>What opened an Exception, in the Canonical Model: a Tracking event or a Proof of delivery, and later a delivery window that passed.</summary>
/// <param name="Kind"><c>TrackingEvent</c> or <c>ProofOfDelivery</c>.</param>
/// <param name="SourceEventId">The Carrier's own ID for the Tracking event, or the document ID of the Proof of delivery.</param>
public sealed record ExceptionCauseV1(string Kind, string? SourceEventId)
{
    public const string TrackingEvent = "TrackingEvent";

    public const string ProofOfDelivery = "ProofOfDelivery";
}
