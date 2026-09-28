using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// Puts the <see cref="HopQuery"/>'s rows together into a trail's hops. Each call is a hop, each Logic App run is one
/// hop however many rows it has, and each record adds its keys to the hop above it. A record written outside any call
/// belongs to the nearest in-process span above it in its application, such as the Partner portal's for sending a
/// booking or the TMS's for rating a Transport Order, which is then a hop with that span's time and name; or else it is
/// a hop of its own.
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

        var spans = rows.Where(row => row.Kind == HopRow.Link && row.InProcess && row.Id is not null)
            .GroupBy(row => row.Id!, StringComparer.Ordinal)
            .ToDictionary(span => span.Key, span => span.First(), StringComparer.Ordinal);

        var hops = new List<Hop>();
        hops.AddRange(rows.Where(row => row.Kind == HopRow.Request && row.Id is not null)
            .Select(row => new Hop(row.Id!, row.ParentId, HopNames.OfCall(row.Role ?? "unknown", row.Name ?? string.Empty, null, row.Path), row.Start, row.End, null, [row])));
        hops.AddRange(rows.Where(row => row.Kind == HopRow.Run && row.RunId is not null && row.Id is not null)
            .GroupBy(row => row.RunId!, StringComparer.Ordinal)
            .Select(Run));

        var callIds = hops.Select(hop => hop.Id).ToHashSet(StringComparer.Ordinal);
        var byId = hops.ToDictionary(hop => hop.Id, StringComparer.Ordinal);
        // A record under no call belongs to the span its application wrote it in, or else is a hop of its own, together
        // with the records its application wrote under the same parent, such as everything logged while the TMS's outbox
        // sent an event.
        var records = rows.Where(row => row.Kind == HopRow.Record).ToList();
        var alone = new List<(string Id, HopRow Record)>();
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (HopCauses.NearestHopAbove(record.ParentId, callIds, ParentOf) is { } above)
            {
                byId[above].Rows.Add(record);
            }
            else if (SpanOf(record, spans, ParentOf) is { } span)
            {
                if (!byId.TryGetValue(span.Id!, out var hop))
                {
                    hop = new Hop(span.Id!, span.ParentId, HopNames.OfSpan(span.Role ?? "unknown", span.Name ?? string.Empty), span.Start, span.End, null, []);
                    byId[span.Id!] = hop;
                    hops.Add(hop);
                }

                hop.Rows.Add(record);
            }
            else
            {
                alone.Add((record.ParentId is { } parent ? $"{record.Role}@{parent}" : $"{record.Role}#{index}", record));
            }
        }

        hops.AddRange(alone.GroupBy(record => record.Id, record => record.Record, StringComparer.Ordinal)
            .Select(together => new Hop(
                together.Key,
                null,
                HopNames.OfRecords(together.First().Role ?? "unknown", together.Select(record => record.EventType).FirstOrDefault(type => type is not null)),
                together.Min(record => record.Start),
                null,
                null,
                [.. together])));

        var hopIds = hops.Select(hop => hop.Id).ToHashSet(StringComparer.Ordinal);
        return [.. hops.Select(hop => hop.ToRecorded(HopCauses.Of(hop.Id, hop.ParentId, hopIds, ParentOf)))];
    }

    /// <summary>The nearest in-process span at or above the record's parent in the record's own application, if any.</summary>
    private static HopRow? SpanOf(HopRow record, Dictionary<string, HopRow> spans, Func<string, string?> parentOf)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var id = record.ParentId; !string.IsNullOrEmpty(id) && seen.Add(id); id = parentOf(id))
        {
            if (spans.TryGetValue(id, out var span) && span.Role == record.Role)
            {
                return span;
            }
        }

        return null;
    }

    // The run's trigger carries the run's ID and the call that started it.
    private static Hop Run(IGrouping<string, HopRow> run)
    {
        var trigger = run.FirstOrDefault(row => row.IsTrigger) ?? run.OrderBy(row => row.Start).First();
        return new Hop(
            trigger.Id!,
            trigger.ParentId,
            HopNames.OfRun(trigger.Role ?? "unknown", trigger.Workflow ?? string.Empty),
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
