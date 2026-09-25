namespace Norvia.Integration.TransportOrderIntake;

/// <summary>
/// The countries Norvia drives to, found by the English name partners write them with, a common variant of it, or
/// the ISO 3166-1 alpha-2 code itself.
/// </summary>
internal static class Countries
{
    private static readonly (Country Country, string[] Names)[] Served =
    [
        (new("NL", "Europe/Amsterdam"), ["Netherlands", "The Netherlands", "Nederland", "Holland"]),
        (new("BE", "Europe/Brussels"), ["Belgium", "België", "Belgique"]),
        (new("LU", "Europe/Luxembourg"), ["Luxembourg"]),
        (new("DE", "Europe/Berlin"), ["Germany", "Deutschland"]),
        (new("FR", "Europe/Paris"), ["France"]),
        (new("GB", "Europe/London"), ["United Kingdom", "UK", "Great Britain"]),
        (new("DK", "Europe/Copenhagen"), ["Denmark"]),
        (new("AT", "Europe/Vienna"), ["Austria"]),
        (new("CH", "Europe/Zurich"), ["Switzerland"]),
        (new("PL", "Europe/Warsaw"), ["Poland"]),
        (new("CZ", "Europe/Prague"), ["Czechia", "Czech Republic"]),
        (new("IT", "Europe/Rome"), ["Italy"]),
        (new("ES", "Europe/Madrid"), ["Spain"]),
    ];

    private static readonly Dictionary<string, Country> ByName = Served
        .SelectMany(served => served.Names.Append(served.Country.Code).Select(name => (name, served.Country)))
        .ToDictionary(entry => entry.name, entry => entry.Country, StringComparer.OrdinalIgnoreCase);

    public static Country? Find(string? name) =>
        name is not null && ByName.TryGetValue(name.Trim(), out var country) ? country : null;
}
