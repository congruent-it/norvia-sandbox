using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Estate health-check agent. It runs the checks, then narrates the Findings the tool returned: it puts them
/// in order of the severity each Check gave them and explains the business impact of each, citing the Finding by its ID. For the
/// dead-letter alert Check, whose missing alert hid the Carrier-tracking incident (P5), the impact is the one the demo tells. A Finding
/// of another Check is explained from its own title and rule, and a run with no Finding says so. It never names a Finding the tool
/// did not return.
/// </summary>
public static class HealthCheckScript
{
    public const string RunChecks = "run_checks";

    /// <summary>The Check whose Finding is a queue or subscription with no alert on its dead-letter queue.</summary>
    public const string DeadLetterAlertCheck = "DLQ-ALERT";

    private static readonly string[] SeverityOrder = ["Critical", "High", "Medium", "Low"];

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        var followUp = HealthCheckQuestions.TryParseFollowUp(context.Question, out var kind, out var findingId);
        if (!followUp && !HealthCheckQuestions.IsNarrate(context.Question))
        {
            return [new TextContent("{}")];
        }

        if (context.Results.Count == 0)
        {
            return [new FunctionCallContent($"call-{context.Turn}", RunChecks, new Dictionary<string, object?>())];
        }

        return [new TextContent(followUp ? FollowUpAnswer(context.Results[0], kind, findingId) : Answer(context.Results[0]))];
    }

    // The answer to one Finding's follow-up: that Finding alone, or nothing when the run no longer holds it (it was fixed meanwhile).
    private static string FollowUpAnswer(ScriptedToolResult result, HealthCheckFollowUp kind, string findingId)
    {
        var finding = FindingsOf(result.Content).Find(candidate => candidate.Id == findingId);
        if (finding is null)
        {
            return JsonSerializer.Serialize(new { summary = $"The health check no longer finds {findingId}.", findings = Array.Empty<object>() });
        }

        var impact = kind == HealthCheckFollowUp.Why
            ? Impact(finding)
            : $"Fixing it puts right what the Check found: {finding.Fix} Then the Check passes and {finding.Id} no longer appears.";
        return JsonSerializer.Serialize(new
        {
            summary = kind == HealthCheckFollowUp.Why ? $"Why {finding.Id} is a problem." : $"What changes when {finding.Id} is fixed.",
            findings = new[] { new { id = finding.Id, impact, citation = result.Id } },
        });
    }

    private static string Answer(ScriptedToolResult result)
    {
        var findings = FindingsOf(result.Content);
        if (findings.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                summary = "The health check found nothing: every Check passed, so there is no Finding to explain.",
                findings = Array.Empty<object>(),
            });
        }

        var ordered = findings.OrderBy(finding => Math.Max(0, Array.IndexOf(SeverityOrder, finding.Severity))).ToList();
        return JsonSerializer.Serialize(new
        {
            summary = ordered.Count == 1
                ? "The health check found one problem, in the estate's alerting."
                : $"The health check found {ordered.Count} problems. They are in order of severity, the most serious first.",
            findings = ordered.Select(finding => new { id = finding.Id, impact = Impact(finding), citation = result.Id }),
        });
    }

    private static string Impact(FindingFacts finding) =>
        finding.CheckId == DeadLetterAlertCheck
            ? $"{Subjects(finding)} no alert watches the dead-letter queue. When a message is dead-lettered there, nobody is told: it waits unnoticed and the business step behind it stops. " +
              (finding.Subjects.Contains("carrier-tracking", StringComparer.Ordinal)
                  ? "This is how the Carrier-tracking incident went unseen: the Carrier's status message dead-lettered, the Shipment stayed In transit, and nobody was told."
                  : "Until the alert exists, the queue has to be looked at by hand.")
            : $"{finding.Title}. The rule this breaks: {finding.Rule}";

    private static string Subjects(FindingFacts finding) =>
        finding.Subjects.Count switch
        {
            0 => "A queue or subscription has",
            1 => $"The queue or subscription {finding.Subjects[0]} has",
            _ => $"{finding.Subjects.Count} queues and subscriptions ({string.Join(", ", finding.Subjects)}) have",
        };

    private static List<FindingFacts> FindingsOf(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return
            [
                .. findings.EnumerateArray().Where(finding => finding.ValueKind == JsonValueKind.Object && Text(finding, "id") is not null).Select(finding =>
                    new FindingFacts(
                        Text(finding, "id")!,
                        Text(finding, "checkId") ?? string.Empty,
                        Text(finding, "severity") ?? string.Empty,
                        Text(finding, "title") ?? "A Check failed",
                        Text(finding, "rule") ?? string.Empty,
                        Text(finding, "fix") ?? string.Empty,
                        SubjectsOf(finding))),
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> SubjectsOf(JsonElement finding) =>
        finding.TryGetProperty("evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Array
            ? [.. evidence.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(item => Text(item, "subject")).OfType<string>()]
            : [];

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record FindingFacts(string Id, string CheckId, string Severity, string Title, string Rule, string Fix, List<string> Subjects);
}
