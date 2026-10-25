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
