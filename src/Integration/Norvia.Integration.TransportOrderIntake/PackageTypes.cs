namespace Norvia.Integration.TransportOrderIntake;

/// <summary>Turns a partner's packaging code (UN/ECE Recommendation 21) into a Canonical Model package type.</summary>
internal static class PackageTypes
{
    private static readonly Dictionary<string, string> ByPackagingCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PX"] = "pallet",
        ["PK"] = "parcel",
        ["CR"] = "crate",
        ["NE"] = "loose",
    };

    public static string? ToCanonical(string? packaging) =>
        packaging is not null && ByPackagingCode.TryGetValue(packaging.Trim(), out var packageType) ? packageType : packaging;
}
