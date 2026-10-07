namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>
/// What the Norvia Partner API answered to a tracking query. A failure is an answer, not an error, because the portal
/// keeps the last answer it had and shows the failure beside it.
/// </summary>
public sealed record TrackingAnswer<T>
{
    internal TrackingAnswer(TrackingOutcome outcome, T? value, string? text, string? messageId)
    {
        Outcome = outcome;
        Value = value;
        Text = text;
        MessageId = messageId;
    }

    public TrackingOutcome Outcome { get; }

    /// <summary>What was asked for; null unless <see cref="Outcome"/> is <see cref="TrackingOutcome.Found"/>.</summary>
    public T? Value { get; }

    /// <summary>Norvia's own words for a failure, or ours when it said none; null for a found answer.</summary>
    public string? Text { get; }

    /// <summary>The correlation ID Norvia gave the query, by which Norvia finds every trace of it. Null when it never answered.</summary>
    public string? MessageId { get; }

    public bool IsFound => Outcome == TrackingOutcome.Found;
}

/// <summary>Makes the answers to tracking queries.</summary>
public static class TrackingAnswer
{
    public static TrackingAnswer<T> Found<T>(T value, string? messageId = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(TrackingOutcome.Found, value, text: null, messageId);
    }

    public static TrackingAnswer<T> Failed<T>(TrackingOutcome outcome, string text, string? messageId = null)
    {
        if (outcome == TrackingOutcome.Found)
        {
            throw new ArgumentException("A found answer has its value, not a failure.", nameof(outcome));
        }

        return new(outcome, value: default, text, messageId);
    }

    public static TrackingAnswer<T> NotFound<T>(string text, string? messageId = null) => Failed<T>(TrackingOutcome.NotFound, text, messageId);

    public static TrackingAnswer<T> Unavailable<T>(string text, string? messageId = null) => Failed<T>(TrackingOutcome.Unavailable, text, messageId);
}
