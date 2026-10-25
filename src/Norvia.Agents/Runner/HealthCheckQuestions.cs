namespace Norvia.Agents.Runner;

/// <summary>
/// The fixed question an operator puts to the Estate health-check agent. Nothing free-form reaches the model (ADR 0040). The scripted
/// model reads the same text back to tell it from another question.
/// </summary>
public static class HealthCheckQuestions
{
    public const string Narrate =
        "Run the estate's health check and narrate its Findings: put them in order, explain in plain language what each one means for the business, and cite the Finding by its ID.";

    public static bool IsNarrate(string question) => string.Equals(question, Narrate, StringComparison.Ordinal);

    private const string WhyPrefix = "Run the estate's health check and explain why the Finding ";
    private const string WhatChangesPrefix = "Run the estate's health check and explain what changes if the Finding ";
    private const string WhySuffix = " is a problem. Answer for that Finding alone, citing it by its ID.";
    private const string WhatChangesSuffix = " is fixed. Answer for that Finding alone, citing it by its ID.";

    /// <summary>The fixed follow-up "why is this a problem?" about one Finding. Only the Finding's ID varies.</summary>
    public static string Why(string findingId) => WhyPrefix + findingId + WhySuffix;

    /// <summary>The fixed follow-up "what changes if I fix it?" about one Finding. Only the Finding's ID varies.</summary>
    public static string WhatChanges(string findingId) => WhatChangesPrefix + findingId + WhatChangesSuffix;

    /// <summary>Reads a follow-up back into the kind it asks and the Finding it is about; false for any other text.</summary>
    public static bool TryParseFollowUp(string question, out HealthCheckFollowUp followUp, out string findingId)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (TryStrip(question, WhyPrefix, WhySuffix, out findingId))
        {
            followUp = HealthCheckFollowUp.Why;
            return true;
        }

        if (TryStrip(question, WhatChangesPrefix, WhatChangesSuffix, out findingId))
        {
            followUp = HealthCheckFollowUp.WhatChanges;
            return true;
        }

        followUp = default;
        return false;
    }

    private static bool TryStrip(string question, string prefix, string suffix, out string findingId)
    {
        findingId = string.Empty;
        if (!question.StartsWith(prefix, StringComparison.Ordinal) || !question.EndsWith(suffix, StringComparison.Ordinal) || question.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        findingId = question[prefix.Length..^suffix.Length];
        return true;
    }
}

/// <summary>The two questions an operator can put about one Finding.</summary>
public enum HealthCheckFollowUp
{
    Why,
    WhatChanges,
}
