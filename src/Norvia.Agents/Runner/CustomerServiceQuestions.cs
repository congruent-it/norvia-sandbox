namespace Norvia.Agents.Runner;

/// <summary>
/// The fixed question Customer service puts to the Customer service agent: one per Explain button. Nothing free-form reaches the model
/// (ADR 0040). The scripted model reads the same text back to tell it from another question.
/// </summary>
public static class CustomerServiceQuestions
{
    private const string Prefix = "Explain why the Rejected booking ";
    private const string Suffix = " was rejected, and what the Shipper should change.";

    public static string ExplainRejectedBooking(Guid id) => Prefix + id.ToString("D") + Suffix;

    /// <summary>The Rejected booking's ID of an Explain question, or null when the text is another question.</summary>
    public static Guid? ParseExplainRejectedBooking(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        return question.StartsWith(Prefix, StringComparison.Ordinal) && question.EndsWith(Suffix, StringComparison.Ordinal)
            && question.Length > Prefix.Length + Suffix.Length
            && Guid.TryParseExact(question[Prefix.Length..^Suffix.Length], "D", out var id)
                ? id
                : null;
    }

    private const string SummaryPrefix = "Summarise the life of the Transport Order ";
    private const string SummarySuffix = " from rating to billing, and say what, if anything, is blocking it.";

    public static string SummariseTransportOrder(string transportOrderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);
        return SummaryPrefix + transportOrderNumber + SummarySuffix;
    }

    /// <summary>The Transport Order number of a summary question, or null when the text is another question.</summary>
    public static string? ParseSummariseTransportOrder(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (!question.StartsWith(SummaryPrefix, StringComparison.Ordinal) || !question.EndsWith(SummarySuffix, StringComparison.Ordinal)
            || question.Length <= SummaryPrefix.Length + SummarySuffix.Length)
        {
            return null;
        }

        var number = question[SummaryPrefix.Length..^SummarySuffix.Length];
        return number.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ? number : null;
    }

    private const string RatingPrefix = "Explain why Rating failed for the Transport Order ";
    private const string RatingSuffix = " and what fixes it.";
    private const string HoldPrefix = "Explain what is holding the Transport Order ";
    private const string HoldSuffix = " back from becoming billable, and which of that Customer service can act on.";

    public static string ExplainRatingFailure(string transportOrderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);
        return RatingPrefix + transportOrderNumber + RatingSuffix;
    }

    /// <summary>The Transport Order number of a rating failure question, or null when the text is another question.</summary>
    public static string? ParseExplainRatingFailure(string question) => ParseNumber(question, RatingPrefix, RatingSuffix);

    public static string ExplainNotYetBillable(string transportOrderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);
        return HoldPrefix + transportOrderNumber + HoldSuffix;
    }

    /// <summary>The Transport Order number of a not-yet-billable question, or null when the text is another question.</summary>
    public static string? ParseExplainNotYetBillable(string question) => ParseNumber(question, HoldPrefix, HoldSuffix);

    private static string? ParseNumber(string question, string prefix, string suffix)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (!question.StartsWith(prefix, StringComparison.Ordinal) || !question.EndsWith(suffix, StringComparison.Ordinal)
            || question.Length <= prefix.Length + suffix.Length)
        {
            return null;
        }

        var number = question[prefix.Length..^suffix.Length];
        return number.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ? number : null;
    }
}
