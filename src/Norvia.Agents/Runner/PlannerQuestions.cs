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

    private const string TallyMiddle = " The Transport Orders already assumed on Own fleet in this plan: ";
    private const string TallyEnd = ".";

    /// <summary>
    /// The question for one order of a plan of the waiting list (#445): the same, with the orders the plan has already suggested for Own fleet.
    /// With none, it is the plain <see cref="SuggestAssignment(string)"/> question.
    /// </summary>
    public static string SuggestAssignment(string transportOrderNumber, IReadOnlyList<string> alreadyAssumed)
    {
        ArgumentNullException.ThrowIfNull(alreadyAssumed);
        return alreadyAssumed.Count == 0
            ? SuggestAssignment(transportOrderNumber)
            : SuggestAssignment(transportOrderNumber) + TallyMiddle + string.Join(", ", alreadyAssumed) + TallyEnd;
    }

    /// <summary>The Transport Order number and the orders already assumed of a Suggest question (with or without a tally), or null when the text is another question.</summary>
    public static (string Number, IReadOnlyList<string> AlreadyAssumed)? ParseSuggestAssignmentWithTally(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (ParseSuggestAssignment(question) is { } plain)
        {
            return (plain, []);
        }

        var at = question.IndexOf(TallyMiddle, StringComparison.Ordinal);
        if (at < 0 || !question.EndsWith(TallyEnd, StringComparison.Ordinal) || ParseSuggestAssignment(question[..at]) is not { } number)
        {
            return null;
        }

        var assumed = question[(at + TallyMiddle.Length)..^TallyEnd.Length].Split(", ");
        return assumed.All(IsKey) ? (number, assumed) : null;
    }

    private static bool IsKey(string text) => text.Length > 0 && text.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

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

    private const string NotifyPrefix = "Draft the Notify Shipper message for the Exception ";
    private const string NotifyMiddle = " on the Transport Order ";
    private const string NotifySuffix = ", from the template and what the Shipper sees.";

    public static string DraftShipperNotification(string exceptionNumber, string transportOrderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exceptionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);
        return NotifyPrefix + exceptionNumber + NotifyMiddle + transportOrderNumber + NotifySuffix;
    }

    /// <summary>The Exception and Transport Order numbers of a Notify Shipper draft question, or null when the text is another question.</summary>
    public static (string ExceptionNumber, string TransportOrderNumber)? ParseDraftShipperNotification(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (!question.StartsWith(NotifyPrefix, StringComparison.Ordinal) || !question.EndsWith(NotifySuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = question[NotifyPrefix.Length..^NotifySuffix.Length].Split(NotifyMiddle);
        return parts.Length == 2 && parts.All(part => part.Length > 0 && part.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            ? (parts[0], parts[1])
            : null;
    }
}
