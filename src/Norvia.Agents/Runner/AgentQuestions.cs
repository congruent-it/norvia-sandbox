using System.Globalization;
using System.Text.RegularExpressions;

namespace Norvia.Agents.Runner;

/// <summary>
/// The fixed questions an operator can put to the Integration operations agent: one per Explain button and one for the "what needs
/// attention" summary. Nothing free-form reaches the model (ADR 0040). The scripted model reads the same text back to tell them apart.
/// </summary>
public static partial class AgentQuestions
{
    public const string WhatNeedsAttention =
        "Which dead-letter queues need attention? List the queues that hold messages and group them by cause, citing what you read, and say what the Integration operator could do about each cause.";

    public static string ExplainTrail(string correlationId) =>
        $"Explain why the Message trail {correlationId} failed, and what the Integration operator could do about it.";

    public static string ExplainFailedRun(string flow, DateTimeOffset start) =>
        $"Explain why the {flow} run that started at {start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)} failed, and what the Integration operator could do about it.";

    public static string ExplainDeadLetter(string entity, long sequenceNumber) =>
        $"Explain why message {sequenceNumber.ToString(CultureInfo.InvariantCulture)} of the dead-letter queue {entity} was dead-lettered, and what the Integration operator could do about it.";

    /// <summary>The flow and start time of a failed-run question, or null when the text is another question.</summary>
    public static (string Flow, DateTimeOffset Start)? ParseFailedRun(string question)
    {
        var match = FailedRun().Match(question);
        return match.Success && DateTimeOffset.TryParse(match.Groups["start"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start)
            ? (match.Groups["flow"].Value, start)
            : null;
    }

    /// <summary>The queue and sequence number of a dead-letter question, or null when the text is another question.</summary>
    public static (string Entity, long SequenceNumber)? ParseDeadLetter(string question)
    {
        var match = DeadLetter().Match(question);
        return match.Success && long.TryParse(match.Groups["sequence"].Value, CultureInfo.InvariantCulture, out var sequence)
            ? (match.Groups["entity"].Value, sequence)
            : null;
    }

    public static bool IsWhatNeedsAttention(string question) => string.Equals(question, WhatNeedsAttention, StringComparison.Ordinal);

    [GeneratedRegex(@"^Explain why the (?<flow>.+) run that started at (?<start>\S+) failed,")]
    private static partial Regex FailedRun();

    [GeneratedRegex(@"^Explain why message (?<sequence>\d+) of the dead-letter queue (?<entity>\S+) was dead-lettered,")]
    private static partial Regex DeadLetter();
}
