using System.Globalization;
using System.Text;
using Norvia.Agents;
using Norvia.HealthCheck.Application.Run;

namespace Norvia.HealthCheck.Application.Report;

/// <summary>
/// The current report as Markdown, for an operator to keep or send. Every Finding is there with its Check ID, severity, evidence and fix;
/// the assistant's words are added only where it explained that Finding, and marked as its.
/// </summary>
public static class MarkdownReport
{
    public static string Render(HealthCheckReport report, Narration? narration = null, ReportComparison? comparison = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        text.Append("# Estate health check\n\n");
        text.Append(CultureInfo.InvariantCulture, $"Estate: {Plain(report.Estate)}  \nRun: {report.RanAt:yyyy-MM-dd HH:mm} UTC  \nChecks run: {report.ChecksRun}  \nFindings: {report.Findings.Count}\n\n");

        if (comparison is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $"Against the previous run: {comparison.New.Count} new, {comparison.Fixed.Count} fixed, {comparison.Unchanged.Count} unchanged.\n\n");
            Section(text, "Fixed since the previous run", comparison.Fixed);
        }

        if (report.Findings.Count == 0)
        {
            text.Append("No Findings. Every Check passed.\n");
            return text.ToString();
        }

        if (!string.IsNullOrWhiteSpace(narration?.Summary))
        {
            text.Append(CultureInfo.InvariantCulture, $"Assistant's ordering: {Plain(narration.Summary)} The severity of each Finding is its Check's own.\n\n");
        }

        var explained = (narration?.Items ?? []).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var isNew = (comparison?.New ?? []).Select(finding => finding.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var group in SeverityGroup.By(report.Findings, narration?.Items.Select(item => item.Id).ToList()))
        {
            text.Append(CultureInfo.InvariantCulture, $"## {group.Severity}\n\n");
            foreach (var finding in group.Findings)
            {
                text.Append(CultureInfo.InvariantCulture, $"### {finding.Id}: {Plain(finding.Title)}{(isNew.Contains(finding.Id) ? " (new)" : string.Empty)}\n\n");
                text.Append(CultureInfo.InvariantCulture, $"- Check: `{finding.CheckId}`\n- Severity: {finding.Severity}\n- Rule: {Plain(finding.Rule)}\n- As-found toggle: `{finding.AsFoundToggle}`\n");
                text.Append("- Evidence:\n");
                foreach (var evidence in finding.Evidence)
                {
                    text.Append(CultureInfo.InvariantCulture, $"  - {Plain(evidence.Subject)}: {Plain(evidence.Detail)}\n");
                }

                text.Append(CultureInfo.InvariantCulture, $"- Fix: {Plain(finding.Fix)}\n");
                if (explained.TryGetValue(finding.Id, out var item))
                {
                    text.Append(CultureInfo.InvariantCulture, $"- Why it matters (the assistant's words): {Plain(item.Impact)} [{item.ResultId}, {item.ToolName}]\n");
                }

                text.Append('\n');
            }
        }

        return text.ToString();
    }

    private static void Section(StringBuilder text, string heading, IReadOnlyList<Checks.Finding> findings)
    {
        if (findings.Count == 0)
        {
            return;
        }

        text.Append(CultureInfo.InvariantCulture, $"## {heading}\n\n");
        foreach (var finding in findings)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {finding.Id}: {Plain(finding.Title)}\n");
        }

        text.Append('\n');
    }

    // One line of text: names and the assistant's words are data, so they can't start a heading or a list item of their own.
    private static string Plain(string value) => value.ReplaceLineEndings(" ").Trim();
}
