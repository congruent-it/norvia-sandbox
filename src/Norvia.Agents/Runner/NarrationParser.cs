using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norvia.Agents.Runner;

/// <summary>
/// Reads the model's final reply as a <see cref="Narration"/>, and refuses one that isn't the expected result, cites a result the run
/// never had, or explains a Finding that result does not hold: the Agent never invents a Finding.
/// </summary>
public static partial class NarrationParser
{
    /// <param name="figuresFromResults">
    /// True for an Agent that must never invent a number (the FinOps agent): every number in the summary and in each impact has to be one a
    /// tool result of the run holds, compared by value (0.90 is 0.9). Anything else is refused as <c>AgentUnavailable</c>.
    /// </param>
    public static Narration Parse(string reply, IReadOnlyDictionary<string, ToolResultText> results, bool figuresFromResults = false)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(results);

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(JsonReplies.WithoutFence(reply));
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new AgentRunException("The reply isn't JSON.", exception);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new AgentRunException("The reply isn't a narration.");
        }

        var summary = Text(root, "summary");
        if (!root.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array)
        {
            throw new AgentRunException("The reply has no findings list.");
        }

        var items = new List<NarratedItem>();
        foreach (var finding in findings.EnumerateArray())
        {
            if (finding.ValueKind != JsonValueKind.Object)
            {
                throw new AgentRunException("A finding of the reply isn't an object.");
            }

            var id = Text(finding, "id");
            var citation = Text(finding, "citation");
            if (!results.TryGetValue(citation, out var result))
            {
                throw new AgentRunException($"The reply cites '{citation}', which is no result of this run.");
            }

            if (!FindingIds(result.Content).Contains(id, StringComparer.Ordinal))
            {
                throw new AgentRunException($"The reply explains the Finding '{id}', which result {citation} does not hold.");
            }

            if (items.Exists(item => string.Equals(item.Id, id, StringComparison.Ordinal)))
            {
                throw new AgentRunException($"The reply explains the Finding '{id}' twice.");
            }

            items.Add(new NarratedItem(id, Text(finding, "impact"), citation, result.ToolName));
        }

        if (figuresFromResults)
        {
            var known = results.Values.SelectMany(result => Figures(result.Content)).ToHashSet();
            foreach (var text in items.Select(item => item.Impact).Prepend(summary))
            {
                if (Figures(text).Where(figure => !known.Contains(figure)).Select(figure => (decimal?)figure).FirstOrDefault() is { } unknown)
                {
                    throw new AgentRunException($"The reply writes the number {unknown.ToString(CultureInfo.InvariantCulture)}, which is not in any result of this run.");
                }
            }
        }

        return new Narration(summary, items);
    }

    // Every number in a text, by value, so 0.90 and 0.9 are the same figure.
    private static IEnumerable<decimal> Figures(string text) =>
        FigurePattern().Matches(text).Select(match => decimal.Parse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture));

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex FigurePattern();

    // The IDs of the Findings a tool result lists: its "findings" array of objects with an "id".
    private static IReadOnlyList<string> FindingIds(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("findings", out var findings)
                && findings.ValueKind == JsonValueKind.Array
                    ? [.. findings.EnumerateArray()
                        .Where(finding => finding.ValueKind == JsonValueKind.Object && finding.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        .Select(finding => finding.GetProperty("id").GetString()!)]
                    : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : throw new AgentRunException($"The reply has no {property}.");
}

/// <summary>What a tool returned, and which tool it was: the run's record of one result.</summary>
public sealed record ToolResultText(string ToolName, string Content);
