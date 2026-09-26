namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>
/// Works out which hop caused another from the telemetry's parent IDs, as every <see cref="Application.Trails.ITrailSource"/>
/// adapter does alike. A hop's parent is seldom a hop itself: between an application's request and the call it made
/// there is its outgoing HTTP call, a database call and the like. So the cause is the nearest hop up the parents.
/// </summary>
internal static class HopCauses
{
    /// <param name="hop">The hop whose cause is wanted.</param>
    /// <param name="parent">Its own parent ID in the telemetry, if it has one.</param>
    /// <param name="hops">The IDs of every hop on the trail.</param>
    /// <param name="parentOf">The parent ID of anything else the telemetry recorded, or null when it has none or isn't known.</param>
    public static string? Of(string hop, string? parent, IReadOnlySet<string> hops, Func<string, string?> parentOf) =>
        NearestHopAbove(parent, hops, parentOf, hop);

    /// <summary>
    /// The nearest hop at or above <paramref name="parent"/>, such as the hop a log record was written in, or null when
    /// there is none.
    /// </summary>
    public static string? NearestHopAbove(string? parent, IReadOnlySet<string> hops, Func<string, string?> parentOf) =>
        NearestHopAbove(parent, hops, parentOf, below: null);

    private static string? NearestHopAbove(string? parent, IReadOnlySet<string> hops, Func<string, string?> parentOf, string? below)
    {
        // Telemetry is not to be trusted with a cycle, so the walk stops at an ID it has already passed.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (below is not null)
        {
            seen.Add(below);
        }

        for (var id = parent; !string.IsNullOrEmpty(id) && seen.Add(id); id = parentOf(id))
        {
            if (hops.Contains(id))
            {
                return id;
            }
        }

        return null;
    }
}
