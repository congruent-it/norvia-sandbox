namespace Norvia.IntegrationConsole.Application.Trails.Open;

/// <summary>
/// Puts a trail's hops in the order the page shows them: in time order, each hop followed by the hops it caused. Where a
/// hop caused more than one, the trail fans out and each branch is indented one level under it; a hop that caused just
/// one is simply followed by it, so a chain of calls reads as a list, not a staircase.
/// </summary>
internal static class TrailOutline
{
    public static IReadOnlyList<TrailHop> Of(IReadOnlyList<RecordedHop> recorded)
    {
        var inTimeOrder = recorded.OrderBy(hop => hop.Time).ThenBy(hop => hop.Id, StringComparer.Ordinal).ToList();
        var ids = inTimeOrder.Select(hop => hop.Id).ToHashSet(StringComparer.Ordinal);
        var caused = inTimeOrder.Where(hop => CauseOf(hop, ids) is not null)
            .ToLookup(hop => CauseOf(hop, ids)!, StringComparer.Ordinal);

        var outline = new List<TrailHop>(inTimeOrder.Count);
        var shown = new HashSet<string>(StringComparer.Ordinal);

        // A hop whose cause isn't on the trail starts a branch of its own. Hops that only claim to cause each other are
        // left over after that, and are shown from the earliest, so nothing recorded goes missing.
        foreach (var hop in inTimeOrder.Where(hop => CauseOf(hop, ids) is null).Concat(inTimeOrder))
        {
            Show(hop, depth: 0);
        }

        return outline;

        void Show(RecordedHop hop, int depth)
        {
            if (!shown.Add(hop.Id))
            {
                return;
            }

            outline.Add(new TrailHop(hop.Name, hop.Flow, hop.Time, hop.Duration, hop.Outcome, hop.FailureCode, depth, hop.Run));
            var consequences = caused[hop.Id].Where(next => !shown.Contains(next.Id)).ToList();
            var branchDepth = consequences.Count > 1 ? depth + 1 : depth;
            foreach (var next in consequences)
            {
                Show(next, branchDepth);
            }
        }
    }

    private static string? CauseOf(RecordedHop hop, HashSet<string> ids) =>
        hop.CausedBy is { } cause && cause != hop.Id && ids.Contains(cause) ? cause : null;
}
