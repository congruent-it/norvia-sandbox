namespace Norvia.Tms.Application.Exceptions.Open;

/// <summary>What opened an Exception, in the Canonical Model: a Tracking event, and later a Proof of delivery or a delivery window that passed.</summary>
/// <param name="Kind"><c>TrackingEvent</c>.</param>
/// <param name="SourceEventId">The Carrier's own ID for the Tracking event.</param>
public sealed record ExceptionCauseV1(string Kind, string? SourceEventId)
{
    public const string TrackingEvent = "TrackingEvent";
}
