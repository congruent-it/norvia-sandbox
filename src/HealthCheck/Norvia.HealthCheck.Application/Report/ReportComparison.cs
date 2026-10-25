using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Run;

namespace Norvia.HealthCheck.Application.Report;

/// <summary>What a re-run changed against the run before it. Held in the page session and stored nowhere.</summary>
/// <param name="New">Findings in this run that the previous run did not have.</param>
/// <param name="Fixed">Findings the previous run had that this run does not, as the previous run showed them.</param>
/// <param name="Unchanged">Findings both runs have, as this run shows them.</param>
public sealed record ReportComparison(IReadOnlyList<Finding> New, IReadOnlyList<Finding> Fixed, IReadOnlyList<Finding> Unchanged)
{
    /// <summary>Compares two runs by Finding ID. A Finding whose evidence changed is still the same Finding, so it is unchanged.</summary>
    public static ReportComparison Between(HealthCheckReport previous, HealthCheckReport current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var before = previous.Findings.Select(finding => finding.Id).ToHashSet(StringComparer.Ordinal);
        var now = current.Findings.Select(finding => finding.Id).ToHashSet(StringComparer.Ordinal);
        return new ReportComparison(
            [.. current.Findings.Where(finding => !before.Contains(finding.Id))],
            [.. previous.Findings.Where(finding => !now.Contains(finding.Id))],
            [.. current.Findings.Where(finding => before.Contains(finding.Id))]);
    }
}
