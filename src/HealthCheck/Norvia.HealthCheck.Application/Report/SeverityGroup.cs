using Norvia.HealthCheck.Application.Checks;

namespace Norvia.HealthCheck.Application.Report;

/// <summary>The Findings of one severity.</summary>
public sealed record SeverityGroup(Severity Severity, IReadOnlyList<Finding> Findings)
{
    /// <summary>
    /// Groups Findings by the severity their Check gave them, the most severe group first, and leaves out a severity with none. Within a
    /// group the Findings follow <paramref name="order"/> (the IDs in the order the Agent put them) and those it did not mention come
    /// after, by ID. The order never moves a Finding to another group: the severity is the Check's.
    /// </summary>
    public static IReadOnlyList<SeverityGroup> By(IEnumerable<Finding> findings, IReadOnlyList<string>? order = null)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var rank = (order ?? []).Select((id, index) => (id, index)).DistinctBy(item => item.id, StringComparer.Ordinal).ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        return
        [
            .. findings
                .GroupBy(finding => finding.Severity)
                .OrderByDescending(group => group.Key)
                .Select(group => new SeverityGroup(
                    group.Key,
                    [.. group.OrderBy(finding => rank.GetValueOrDefault(finding.Id, int.MaxValue)).ThenBy(finding => finding.Id, StringComparer.Ordinal)])),
        ];
    }
}
