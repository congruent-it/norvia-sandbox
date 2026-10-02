namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>Norvia's reply to a Carrier's answer: what happened to it, and the words Norvia gave.</summary>
public sealed record NorviaReply
{
    private NorviaReply(CarrierAnswerResult result, string? messageId, string? text, bool repeated)
    {
        Result = result;
        MessageId = messageId;
        Text = text;
        Repeated = repeated;
    }

    public CarrierAnswerResult Result { get; }

    /// <summary>The correlation ID Norvia gave the answer, by which Norvia finds every trace of it. Null when Norvia never replied.</summary>
    public string? MessageId { get; }

    /// <summary>Norvia's own words for a refusal; null for a recorded answer.</summary>
    public string? Text { get; }

    /// <summary>True when Norvia already had the same answer, and recorded nothing again.</summary>
    public bool Repeated { get; }

    public bool IsRecorded => Result == CarrierAnswerResult.Recorded;

    public static NorviaReply Recorded(string? messageId, bool repeated = false) => new(CarrierAnswerResult.Recorded, messageId, null, repeated);

    public static NorviaReply NotReceived(string? messageId) => new(CarrierAnswerResult.NotReceived, messageId, null, repeated: false);

    public static NorviaReply Refusal(CarrierAnswerResult result, string text, string? messageId)
    {
        if (result is CarrierAnswerResult.Recorded or CarrierAnswerResult.NotReceived)
        {
            throw new ArgumentOutOfRangeException(nameof(result), result, "A refusal is not a recorded answer or one that wasn't received.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new NorviaReply(result, messageId, text, repeated: false);
    }
}
