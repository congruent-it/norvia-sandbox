namespace Norvia.Erp.Domain.RateCards;

/// <summary>What a Rate Card's or a Carrier Rate Card's whole list of dated versions must satisfy, and how two lists compare (E3).</summary>
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

    /// <summary>True when both lists hold the same versions, whatever their order, down to every Zone, Lane and Weight Break.</summary>
    public static bool Same(IReadOnlyList<RateCardVersion> left, IReadOnlyList<RateCardVersion> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.Count == right.Count
            && left.OrderBy(version => version.Version).Zip(right.OrderBy(version => version.Version), Same).All(same => same);
    }

    /// <summary>The list in the order the ERP keeps and publishes it: by version number.</summary>
    public static IReadOnlyList<RateCardVersion> Ordered(IReadOnlyList<RateCardVersion> versions) =>
        [.. versions.OrderBy(version => version.Version)];

    private static bool Same(RateCardVersion left, RateCardVersion right) =>
        left.Version == right.Version
        && left.ValidFrom == right.ValidFrom
        && left.ValidTo == right.ValidTo
        && left.Zones.Count == right.Zones.Count
        && left.Zones.Zip(right.Zones, (a, b) => a.Name == b.Name && a.Country == b.Country && a.PostcodePrefixes.SequenceEqual(b.PostcodePrefixes)).All(same => same)
        && left.Lanes.Count == right.Lanes.Count
        && left.Lanes.Zip(right.Lanes, (a, b) => a.Origin == b.Origin && a.Destination == b.Destination && a.MinimumCharge == b.MinimumCharge
            && a.WeightBreaks.SequenceEqual(b.WeightBreaks)).All(same => same);
}
