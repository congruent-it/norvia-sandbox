using System.Globalization;
using Norvia.HealthCheck.Application.Cost;
using Norvia.HealthCheck.Application.Failures;
using Norvia.HealthCheck.Application.Run;

namespace Norvia.HealthCheck.Web.Agents;

/// <summary>Maps the use case's report to the agent view, field by field.</summary>
internal static class AgentViewMapping
{
    public static AgentFindingsView Of(HealthCheckReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new AgentFindingsView(
            report.Estate,
            report.ChecksRun,
            [
                .. report.Findings.Select(finding => new AgentFindingView(
                    finding.Id,
                    finding.CheckId,
                    finding.Title,
                    finding.Rule,
                    finding.Severity.ToString(),
                    [.. finding.Evidence.Select(evidence => new AgentEvidenceView(evidence.Subject, evidence.Detail))],
                    finding.AsFoundToggle,
                    finding.Fix)),
            ]);
    }

    /// <summary>The Findings of one Check alone, from a whole run's report.</summary>
    public static AgentFindingsView Of(HealthCheckReport report, string checkId)
    {
        ArgumentNullException.ThrowIfNull(report);

        var all = Of(report);
        return all with { Findings = [.. all.Findings.Where(finding => finding.CheckId == checkId)] };
    }

    public static AgentCostView Of(CostView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new AgentCostView(view.PeriodDays, view.HasProvisionalDays, Of(view.Total), Of(view.Volume), [.. view.Resources.Select(Of)]);
    }

    private static AgentChangeView Of(PeriodChange change) => new(change.Name, change.Previous, change.Current, Percent(change.Relative));

    // Text, so the agent copies it: "+80.5%", "-5%" or "n/a" where either period has no figure. One decimal, so a small change never shows as 0% and 49.6% never as 50%.
    private static string Percent(decimal? relative) =>
        relative is null
            ? "n/a"
            : (relative.Value > 0 ? "+" : relative.Value < 0 ? "-" : string.Empty) + (Math.Round(Math.Abs(relative.Value) * 100, 1)).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public static AgentFailedRunsView Of(FailedRunsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new AgentFailedRunsView(
            report.Estate,
            report.WindowDays,
            [.. report.Flows.Select(flow => new AgentFailedFlowView(flow.Flow, flow.FailedRuns, flow.HasFailedRunAlert))]);
    }

    public static AgentDeadLetterQueuesView Of(DeadLetterQueuesReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new AgentDeadLetterQueuesView(
            report.Estate,
            [.. report.Queues.Select(queue => new AgentDeadLetterQueueView(queue.Name, queue.DeadLetteredMessages, queue.HasDeadLetterAlert))]);
    }
}
