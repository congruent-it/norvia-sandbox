namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>What Norvia's Partner API answered to a Tracking event. It accepts one and says no more (ADR 0008).</summary>
public sealed record TrackingReply
{
    private TrackingReply(TrackingResult result, string? messageId, string? text)
    {
        Result = result;
        MessageId = messageId;
        Text = text;
    }

    public TrackingResult Result { get; }

    /// <summary>The correlation ID API Management gave the event, by which Norvia finds every trace of it. Null when Norvia never replied.</summary>
    public string? MessageId { get; }

    /// <summary>Norvia's own words for a refusal; null otherwise.</summary>
    public string? Text { get; }

    public bool IsAccepted => Result == TrackingResult.Accepted;

    public static TrackingReply Accepted(string? messageId) => new(TrackingResult.Accepted, messageId, null);

    public static TrackingReply NotReceived(string? messageId) => new(TrackingResult.NotReceived, messageId, null);

    public static TrackingReply Refused(string text, string? messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TrackingReply(TrackingResult.Refused, messageId, text);
    }
}
