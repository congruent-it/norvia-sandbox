using System.Text.RegularExpressions;

namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

/// <summary>What went wrong, as the Shipper notification format names it (P7): <c>COLLECTION_FAILED</c> and so on.</summary>
public enum ShipperExceptionCode
{
    CollectionFailed,
    DeliveryFailed,
    Damaged,
    Reservations,
    Late,
}

public static partial class ShipperExceptionCodes
{
    private static readonly Dictionary<string, ShipperExceptionCode> ByWireName =
        Enum.GetValues<ShipperExceptionCode>().ToDictionary(WireNameOf, StringComparer.Ordinal);

    /// <summary>Every code as the format spells it, which is its name in capitals with underscores: the one place the spelling is made.</summary>
    public static IReadOnlyList<string> WireNames { get; } = [.. ByWireName.Keys];

    public static string WireNameOf(ShipperExceptionCode code) => WordBoundary().Replace(code.ToString(), "_").ToUpperInvariant();

    /// <summary>The code the format spells so, or false for any other text.</summary>
    public static bool TryParse(string? wire, out ShipperExceptionCode code)
    {
        code = default;
        return wire is not null && ByWireName.TryGetValue(wire, out code);
    }

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
