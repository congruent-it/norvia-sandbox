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
}
