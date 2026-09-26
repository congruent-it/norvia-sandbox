using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Infrastructure.Trails;

/// <summary>What every <see cref="ITrailSource"/> adapter does alike with the trails it finds.</summary>
internal static class FoundTrails
{
    /// <summary>At most this many trails, the latest first: a key that matches more is too broad to read anyway.</summary>
    public const int Maximum = 200;

    /// <summary>
    /// A recorded outcome as the tracking contract names it (coding guidelines §7). An outcome outside the contract is a
    /// flow's mistake; the trail is still worth showing without it.
    /// </summary>
    public static Outcome? OutcomeOf(string outcome) =>
        Enum.TryParse<Outcome>(outcome, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
}
