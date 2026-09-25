namespace Norvia.Integration.TransportOrderIntake;

/// <summary>
/// Translates the country of a partner address to its ISO 3166-1 alpha-2 code and the time zone its local times are
/// in. It knows the countries of Europe by their English name, their own name, a common variant, or the code itself.
/// It doesn't decide where Norvia carries goods: a known country is passed on whether Norvia serves it or not.
/// </summary>
/// <remarks>
/// Countries with more than one time zone use their mainland's (Portugal without the Azores, Spain without the
/// Canary Islands). Russia, whose European part alone spans several time zones, isn't known.
/// </remarks>
internal static class Countries
{
    private static readonly (Country Country, string[] Names)[] Known =
    [
        (new("AL", "Europe/Tirane"), ["Albania", "Shqipëria"]),
        (new("AD", "Europe/Andorra"), ["Andorra"]),
        (new("AT", "Europe/Vienna"), ["Austria", "Österreich"]),
        (new("BY", "Europe/Minsk"), ["Belarus"]),
        (new("BE", "Europe/Brussels"), ["Belgium", "België", "Belgique", "Belgien"]),
        (new("BA", "Europe/Sarajevo"), ["Bosnia and Herzegovina", "Bosna i Hercegovina"]),
        (new("BG", "Europe/Sofia"), ["Bulgaria", "България"]),
        (new("HR", "Europe/Zagreb"), ["Croatia", "Hrvatska"]),
        (new("CY", "Asia/Nicosia"), ["Cyprus"]),
        (new("CZ", "Europe/Prague"), ["Czechia", "Czech Republic", "Česko"]),
        (new("DK", "Europe/Copenhagen"), ["Denmark", "Danmark"]),
        (new("EE", "Europe/Tallinn"), ["Estonia", "Eesti"]),
        (new("FI", "Europe/Helsinki"), ["Finland", "Suomi"]),
        (new("FR", "Europe/Paris"), ["France"]),
        (new("DE", "Europe/Berlin"), ["Germany", "Deutschland"]),
        (new("GR", "Europe/Athens"), ["Greece", "Ελλάδα"]),
        (new("HU", "Europe/Budapest"), ["Hungary", "Magyarország"]),
        (new("IS", "Atlantic/Reykjavik"), ["Iceland", "Ísland"]),
        (new("IE", "Europe/Dublin"), ["Ireland", "Éire"]),
        (new("IT", "Europe/Rome"), ["Italy", "Italia"]),
        (new("LV", "Europe/Riga"), ["Latvia", "Latvija"]),
        (new("LI", "Europe/Vaduz"), ["Liechtenstein"]),
        (new("LT", "Europe/Vilnius"), ["Lithuania", "Lietuva"]),
        (new("LU", "Europe/Luxembourg"), ["Luxembourg", "Luxemburg", "Lëtzebuerg"]),
        (new("MT", "Europe/Malta"), ["Malta"]),
        (new("MD", "Europe/Chisinau"), ["Moldova"]),
        (new("MC", "Europe/Monaco"), ["Monaco"]),
        (new("ME", "Europe/Podgorica"), ["Montenegro", "Crna Gora"]),
        (new("NL", "Europe/Amsterdam"), ["Netherlands", "The Netherlands", "Nederland", "Holland"]),
        (new("MK", "Europe/Skopje"), ["North Macedonia", "Macedonia"]),
        (new("NO", "Europe/Oslo"), ["Norway", "Norge"]),
        (new("PL", "Europe/Warsaw"), ["Poland", "Polska"]),
        (new("PT", "Europe/Lisbon"), ["Portugal"]),
        (new("RO", "Europe/Bucharest"), ["Romania", "România"]),
        (new("SM", "Europe/San_Marino"), ["San Marino"]),
        (new("RS", "Europe/Belgrade"), ["Serbia", "Srbija"]),
        (new("SK", "Europe/Bratislava"), ["Slovakia", "Slovensko"]),
        (new("SI", "Europe/Ljubljana"), ["Slovenia", "Slovenija"]),
        (new("ES", "Europe/Madrid"), ["Spain", "España"]),
        (new("SE", "Europe/Stockholm"), ["Sweden", "Sverige"]),
        (new("CH", "Europe/Zurich"), ["Switzerland", "Schweiz", "Suisse", "Svizzera"]),
        (new("TR", "Europe/Istanbul"), ["Turkey", "Türkiye"]),
        // The old spelling: newer time zone databases keep it as a link to Europe/Kyiv, older ones know only this one.
        (new("UA", "Europe/Kiev"), ["Ukraine", "Україна"]),
        (new("GB", "Europe/London"), ["United Kingdom", "UK", "Great Britain", "England", "Scotland", "Wales", "Northern Ireland"]),
    ];

    private static readonly Dictionary<string, Country> ByName = Known
        .SelectMany(known => known.Names.Append(known.Country.Code).Select(name => (name, known.Country)))
        .ToDictionary(entry => entry.name, entry => entry.Country, StringComparer.OrdinalIgnoreCase);

    public static Country? Find(string? name) =>
        name is not null && ByName.TryGetValue(name.Trim(), out var country) ? country : null;
}
