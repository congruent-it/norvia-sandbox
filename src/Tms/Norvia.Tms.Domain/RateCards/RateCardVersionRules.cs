namespace Norvia.Tms.Domain.RateCards;

/// <summary>What a replaced Rate Card's or Carrier Rate Card's whole list of dated versions must satisfy (E3).</summary>
public static class RateCardVersionRules
{
    /// <summary>
    /// Why the list can't be a card's versions, or null when it can: it has none, two share a version number, one ends before it
    /// starts, or two overlap in time. A version with no end is valid for ever.
    /// </summary>
    public static string? Problem(IReadOnlyList<RateCardVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (versions.Count == 0)
        {
            return "A card needs at least one version.";
        }

        if (versions.GroupBy(version => version.Version).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
        {
            return $"Version {duplicate.Key} appears more than once.";
        }

        if (versions.FirstOrDefault(version => version.ValidTo < version.ValidFrom) is { } backwards)
        {
            return $"Version {backwards.Version} ends before it starts.";
        }

        var ordered = versions.OrderBy(version => version.ValidFrom).ToList();
        for (var index = 1; index < ordered.Count; index++)
        {
            var earlier = ordered[index - 1];
            var later = ordered[index];
            if (earlier.ValidTo is null || earlier.ValidTo >= later.ValidFrom)
            {
                return $"Versions {earlier.Version} and {later.Version} overlap in time; versions must not overlap.";
            }
        }

        return null;
    }
}
