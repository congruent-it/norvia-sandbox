namespace Norvia.Agents.Runner;

/// <summary>
/// The fixed question a Planner puts to the Planner agent: one per Suggest button. Nothing free-form reaches the model (ADR 0040). The
/// scripted model reads the same text back to tell it from another question.
/// </summary>
public static class PlannerQuestions
{
    private const string Prefix = "Suggest who should carry the Transport Order ";
    private const string Suffix = ", with the reasoning and the trade-off against the other options.";

    public static string SuggestAssignment(string transportOrderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);
        return Prefix + transportOrderNumber + Suffix;
    }

    /// <summary>The Transport Order number of a Suggest question, or null when the text is another question.</summary>
    public static string? ParseSuggestAssignment(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (!question.StartsWith(Prefix, StringComparison.Ordinal) || !question.EndsWith(Suffix, StringComparison.Ordinal)
            || question.Length <= Prefix.Length + Suffix.Length)
        {
            return null;
        }

        var number = question[Prefix.Length..^Suffix.Length];
        return number.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ? number : null;
    }

    private const string NotePrefix = "Draft a short internal note on the Exception ";
    private const string NoteSuffix = ", of what has happened and what is outstanding.";

    public static string DraftExceptionNote(string exceptionNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exceptionNumber);
        return NotePrefix + exceptionNumber + NoteSuffix;
    }

    /// <summary>The Exception number of a Draft note question, or null when the text is another question.</summary>
    public static string? ParseDraftExceptionNote(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (!question.StartsWith(NotePrefix, StringComparison.Ordinal) || !question.EndsWith(NoteSuffix, StringComparison.Ordinal)
            || question.Length <= NotePrefix.Length + NoteSuffix.Length)
        {
            return null;
        }

        var number = question[NotePrefix.Length..^NoteSuffix.Length];
        return number.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ? number : null;
    }
}
