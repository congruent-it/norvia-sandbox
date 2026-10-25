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
}
