namespace Norvia.Integration.CarrierBooking;

/// <summary>The ISO 3166-1 alpha-3 code of each country the intake map knows by its alpha-2 code.</summary>
internal static class Alpha3Codes
{
    private static readonly Dictionary<string, string> ByAlpha2 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "ALB",
        ["AD"] = "AND",
        ["AT"] = "AUT",
        ["BY"] = "BLR",
        ["BE"] = "BEL",
        ["BA"] = "BIH",
        ["BG"] = "BGR",
        ["HR"] = "HRV",
        ["CY"] = "CYP",
        ["CZ"] = "CZE",
        ["DK"] = "DNK",
        ["EE"] = "EST",
        ["FI"] = "FIN",
        ["FR"] = "FRA",
        ["DE"] = "DEU",
        ["GR"] = "GRC",
        ["HU"] = "HUN",
        ["IS"] = "ISL",
        ["IE"] = "IRL",
        ["IT"] = "ITA",
        ["LV"] = "LVA",
        ["LI"] = "LIE",
        ["LT"] = "LTU",
        ["LU"] = "LUX",
        ["MT"] = "MLT",
        ["MD"] = "MDA",
        ["MC"] = "MCO",
        ["ME"] = "MNE",
        ["NL"] = "NLD",
        ["MK"] = "MKD",
        ["NO"] = "NOR",
        ["PL"] = "POL",
        ["PT"] = "PRT",
        ["RO"] = "ROU",
        ["SM"] = "SMR",
        ["RS"] = "SRB",
        ["SK"] = "SVK",
        ["SI"] = "SVN",
        ["ES"] = "ESP",
        ["SE"] = "SWE",
        ["CH"] = "CHE",
        ["TR"] = "TUR",
        ["UA"] = "UKR",
        ["GB"] = "GBR",
    };

    public static IEnumerable<string> Alpha2 => ByAlpha2.Keys;

    private static readonly Dictionary<string, string> ByAlpha3 = ByAlpha2.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public static string? Alpha2Of(string? alpha3) => alpha3 is not null && ByAlpha3.TryGetValue(alpha3, out var alpha2) ? alpha2 : null;

    public static string? Of(string? alpha2) => alpha2 is not null && ByAlpha2.TryGetValue(alpha2, out var alpha3) ? alpha3 : null;
}
