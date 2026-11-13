using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the FinOps agent. It reads the cost view, then the rising-spend Findings, and narrates each Finding from its own
/// evidence: the resource, the as-found toggle and the Check's fix. Every number it writes is copied from a tool result as written, and a
/// run with no Finding says so. It never names a Finding the tool did not return.
/// </summary>
public static class FinOpsScript
{
    public const string CostView = "get_cost_view";

    public const string RisingSpend = "get_rising_spend_findings";

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (!FinOpsQuestions.IsNarrate(context.Question))
        {
            return [new TextContent("{}")];
        }

        return context.Results.Count switch
        {
            0 => [new FunctionCallContent($"call-{context.Turn}", CostView, new Dictionary<string, object?>())],
            1 => [new FunctionCallContent($"call-{context.Turn}", RisingSpend, new Dictionary<string, object?>())],
            _ => [new TextContent(Answer(context.Results[0], context.Results[1]))],
        };
    }

    private static string Answer(ScriptedToolResult costView, ScriptedToolResult risingSpend)
    {
        var findings = FindingsOf(risingSpend.Content);
        var total = TotalChange(costView.Content);
        var overall = total is null ? "There is no total for the spend in the cost view." : $"Total daily spend changed {total} against the days before.";

        if (findings.Count == 0)
        {
            return JsonSerializer.Serialize(new { summary = $"{overall} There is no rise in spend with flat volume: no resource's spend rises without more Transport Orders behind it.", findings = Array.Empty<object>() });
        }

        return JsonSerializer.Serialize(new
        {
            summary = $"{overall} {findings.Count.ToString(CultureInfo.InvariantCulture)} resource(s) rose while the number of Transport Orders stayed flat.",
            findings = findings.Select(finding => new { id = finding.Id, impact = Impact(finding), citation = risingSpend.Id }),
        });
    }

    private static string Impact(FindingFacts finding) =>
        $"{string.Join(" ", finding.Subjects.Zip(finding.Details, (subject, detail) => $"{subject}: {detail}"))} " +
        $"The spend rose while the number of Transport Orders stayed flat, so no business reason explains it. " +
        $"The as-found toggle behind it is {finding.Toggle}. {finding.Fix}";

    private static string? TotalChange(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("total", out var total)
                && total.ValueKind == JsonValueKind.Object
                    ? Text(total, "change")
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
                {
                    var evidence = finding.TryGetProperty("evidence", out var items) && items.ValueKind == JsonValueKind.Array
                        ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList()
                        : [];
                    return new FindingFacts(
                        Text(finding, "id")!,
                        Text(finding, "asFoundToggle") ?? "not named",
                        Text(finding, "fix") ?? string.Empty,
                        [.. evidence.Select(item => Text(item, "subject") ?? string.Empty)],
                        [.. evidence.Select(item => Text(item, "detail") ?? string.Empty)]);
                }),
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record FindingFacts(string Id, string Toggle, string Fix, List<string> Subjects, List<string> Details);
}
