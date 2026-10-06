namespace Norvia.Tms.Application.Exceptions.Open;

/// <summary>What opened an Exception, in the Canonical Model: a Tracking event or a Proof of delivery, or a delivery window that passed.</summary>
/// <param name="Kind"><c>TrackingEvent</c>, <c>ProofOfDelivery</c> or <c>WindowPassed</c>.</param>
/// <param name="SourceEventId">The Carrier's own ID for the Tracking event, or the document ID of the Proof of delivery; null for a window that passed.</param>
public sealed record ExceptionCauseV1(string Kind, string? SourceEventId);
