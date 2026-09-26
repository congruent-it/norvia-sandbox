using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// Puts the <see cref="HopQuery"/>'s rows together into a trail's hops. Each call is a hop, each Logic App run is one
/// hop however many rows it has, and each record adds its keys to the hop above it, or is a hop of its own when it was
/// written outside any.
/// </summary>
internal static class TrailHops
{
    public static IReadOnlyList<RecordedHop> Of(IReadOnlyList<HopRow> rows)
    {
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var row in rows.Where(row => row.Id is not null))
        {
            parents.TryAdd(row.Id!, row.ParentId);
        }

        string? ParentOf(string id) => parents.GetValueOrDefault(id);

        var hops = new List<Hop>();
        hops.AddRange(rows.Where(row => row.Kind == HopRow.Request && row.Id is not null)
            .Select(row => new Hop(row.Id!, row.ParentId, $"{row.Role}: {row.Name}", row.Start, row.End, null, [row])));
        hops.AddRange(rows.Where(row => row.Kind == HopRow.Run && row.RunId is not null && row.Id is not null)
            .GroupBy(row => row.RunId!, StringComparer.Ordinal)
            .Select(Run));

        var hopIds = hops.Select(hop => hop.Id).ToHashSet(StringComparer.Ordinal);
        var byId = hops.ToDictionary(hop => hop.Id, StringComparer.Ordinal);
        // A record under no hop is a hop of its own, together with the records its application wrote under the same
        // parent, such as everything logged while the TMS's outbox sent an event.
        var records = rows.Where(row => row.Kind == HopRow.Record).ToList();
        var alone = new List<(string Id, HopRow Record)>();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (HopCauses.NearestHopAbove(record.ParentId, hopIds, ParentOf) is { } above)
            {
                byId[above].Rows.Add(record);
            }
            else
            {
                alone.Add((record.ParentId is { } parent ? $"{record.Role}@{parent}" : $"{record.Role}#{index}", record));
            }
        }

        hops.AddRange(alone.GroupBy(record => record.Id, record => record.Record, StringComparer.Ordinal)
            .Select(together => new Hop(together.Key, null, together.First().Role ?? "unknown", together.Min(record => record.Start), null, null, [.. together])));

        return [.. hops.Select(hop => hop.ToRecorded(HopCauses.Of(hop.Id, hop.ParentId, hopIds, ParentOf)))];
    }

    // The run's trigger carries the run's ID and the call that started it.
    private static Hop Run(IGrouping<string, HopRow> run)
    {
        var trigger = run.FirstOrDefault(row => row.IsTrigger) ?? run.OrderBy(row => row.Start).First();
        return new Hop(
            trigger.Id!,
            trigger.ParentId,
            $"{trigger.Role}: {trigger.Workflow}",
            run.Min(row => row.Start),
            run.Max(row => row.End),
            new WorkflowRun(trigger.Role ?? string.Empty, trigger.Workflow ?? string.Empty, run.Key),
            [.. run]);
    }

    private sealed record Hop(string Id, string? ParentId, string Name, DateTimeOffset? Start, DateTimeOffset? End, WorkflowRun? Run, List<HopRow> Rows)
    {
        public RecordedHop ToRecorded(string? causedBy)
        {
            var (outcome, failureCode) = HopOutcomes.Last(Rows.Select(row => (row.Start, row.Outcome, row.FailureCode)));
            return new RecordedHop(
                Id,
                causedBy,
                Name,
                Rows.Select(row => row.Flow).FirstOrDefault(flow => flow is not null),
                Start ?? default,
                Start is { } start && End is { } end && end > start ? end - start : null,
                outcome,
                failureCode,
                [.. Rows.SelectMany(row => row.BusinessKeys).Distinct()],
                Run);
        }
    }
}
