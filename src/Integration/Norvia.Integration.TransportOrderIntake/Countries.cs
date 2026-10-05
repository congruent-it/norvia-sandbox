namespace Norvia.Integration.TransportOrderIntake;

/// <summary>
/// Translates the country of a partner address to its ISO 3166-1 alpha-2 code and the time zone its local times are
/// in. It knows the countries of Europe by their English name, their own name, a common variant, or the code itself.
/// It doesn't decide where Norvia carries goods: a known country is passed on whether Norvia serves it or not.
/// </summary>
/// <remarks>
/// Countries with more than one time zone use their mainland's, except for the islands whose postcodes say they are
/// elsewhere (the Azores, the Canary Islands): <see cref="At"/> gives those their own. Russia, whose European part alone
/// spans several time zones, isn't known.
/// </remarks>
internal static class Countries
{
    private static readonly (Country Country, string[] Names)[] Known =
    [
        (new("AL", "Europe/Tirane", "Central Europe Standard Time"), ["Albania", "Shqipëria"]),
        (new("AD", "Europe/Andorra", "W. Europe Standard Time"), ["Andorra"]),
        (new("AT", "Europe/Vienna", "W. Europe Standard Time"), ["Austria", "Österreich"]),
        (new("BY", "Europe/Minsk", "Belarus Standard Time"), ["Belarus"]),
        (new("BE", "Europe/Brussels", "Romance Standard Time"), ["Belgium", "België", "Belgique", "Belgien"]),
        (new("BA", "Europe/Sarajevo", "Central European Standard Time"), ["Bosnia and Herzegovina", "Bosna i Hercegovina"]),
        (new("BG", "Europe/Sofia", "FLE Standard Time"), ["Bulgaria", "България"]),
        (new("HR", "Europe/Zagreb", "Central European Standard Time"), ["Croatia", "Hrvatska"]),
        (new("CY", "Asia/Nicosia", "GTB Standard Time"), ["Cyprus"]),
        (new("CZ", "Europe/Prague", "Central Europe Standard Time"), ["Czechia", "Czech Republic", "Česko"]),
        (new("DK", "Europe/Copenhagen", "Romance Standard Time"), ["Denmark", "Danmark"]),
        (new("EE", "Europe/Tallinn", "FLE Standard Time"), ["Estonia", "Eesti"]),
        (new("FI", "Europe/Helsinki", "FLE Standard Time"), ["Finland", "Suomi"]),
        (new("FR", "Europe/Paris", "Romance Standard Time"), ["France"]),
        (new("DE", "Europe/Berlin", "W. Europe Standard Time"), ["Germany", "Deutschland"]),
        (new("GR", "Europe/Athens", "GTB Standard Time"), ["Greece", "Ελλάδα"]),
        (new("HU", "Europe/Budapest", "Central Europe Standard Time"), ["Hungary", "Magyarország"]),
        (new("IS", "Atlantic/Reykjavik", "Greenwich Standard Time"), ["Iceland", "Ísland"]),
        (new("IE", "Europe/Dublin", "GMT Standard Time"), ["Ireland", "Éire"]),
        (new("IT", "Europe/Rome", "W. Europe Standard Time"), ["Italy", "Italia"]),
        (new("LV", "Europe/Riga", "FLE Standard Time"), ["Latvia", "Latvija"]),
        (new("LI", "Europe/Vaduz", "W. Europe Standard Time"), ["Liechtenstein"]),
        (new("LT", "Europe/Vilnius", "FLE Standard Time"), ["Lithuania", "Lietuva"]),
        (new("LU", "Europe/Luxembourg", "W. Europe Standard Time"), ["Luxembourg", "Luxemburg", "Lëtzebuerg"]),
        (new("MT", "Europe/Malta", "W. Europe Standard Time"), ["Malta"]),
        (new("MD", "Europe/Chisinau", "E. Europe Standard Time"), ["Moldova"]),
        (new("MC", "Europe/Monaco", "W. Europe Standard Time"), ["Monaco"]),
        (new("ME", "Europe/Podgorica", "Central Europe Standard Time"), ["Montenegro", "Crna Gora"]),
        (new("NL", "Europe/Amsterdam", "W. Europe Standard Time"), ["Netherlands", "The Netherlands", "Nederland", "Holland"]),
        (new("MK", "Europe/Skopje", "Central European Standard Time"), ["North Macedonia", "Macedonia"]),
        (new("NO", "Europe/Oslo", "W. Europe Standard Time"), ["Norway", "Norge"]),
        (new("PL", "Europe/Warsaw", "Central European Standard Time"), ["Poland", "Polska"]),
        (new("PT", "Europe/Lisbon", "GMT Standard Time"), ["Portugal"]),
        (new("RO", "Europe/Bucharest", "GTB Standard Time"), ["Romania", "România"]),
        (new("SM", "Europe/San_Marino", "W. Europe Standard Time"), ["San Marino"]),
        (new("RS", "Europe/Belgrade", "Central Europe Standard Time"), ["Serbia", "Srbija"]),
        (new("SK", "Europe/Bratislava", "Central Europe Standard Time"), ["Slovakia", "Slovensko"]),
        (new("SI", "Europe/Ljubljana", "Central Europe Standard Time"), ["Slovenia", "Slovenija"]),
        (new("ES", "Europe/Madrid", "Romance Standard Time"), ["Spain", "España"]),
        (new("SE", "Europe/Stockholm", "W. Europe Standard Time"), ["Sweden", "Sverige"]),
        (new("CH", "Europe/Zurich", "W. Europe Standard Time"), ["Switzerland", "Schweiz", "Suisse", "Svizzera"]),
        (new("TR", "Europe/Istanbul", "Turkey Standard Time"), ["Turkey", "Türkiye"]),
        // The old spelling: newer time zone databases keep it as a link to Europe/Kyiv, older ones know only this one.
        (new("UA", "Europe/Kiev", "FLE Standard Time"), ["Ukraine", "Україна"]),
        (new("GB", "Europe/London", "GMT Standard Time"), ["United Kingdom", "UK", "Great Britain", "England", "Scotland", "Wales", "Northern Ireland"]),
    ];

    /// <summary>The parts of a country in another time zone than its mainland, found by postcode prefix.</summary>
    private static readonly (string Code, Func<string, bool> HasPostcode, Country Region)[] Regions =
    [
        // Azores: 95xx to 99xx.
        ("PT", postcode => postcode.Length >= 2 && postcode[0] == '9' && postcode[1] >= '5', new("PT", "Atlantic/Azores", "Azores Standard Time")),
        // Canary Islands: the provinces of Las Palmas (35) and Santa Cruz de Tenerife (38).
        ("ES", postcode => postcode.StartsWith("35", StringComparison.Ordinal) || postcode.StartsWith("38", StringComparison.Ordinal), new("ES", "Atlantic/Canary", "GMT Standard Time")),
    ];

    private static readonly Dictionary<string, Country> ByName = Known
        .SelectMany(known => known.Names.Append(known.Country.Code).Select(name => (name, known.Country)))
        .ToDictionary(entry => entry.name, entry => entry.Country, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<Country> All => Known.Select(known => known.Country);

    /// <summary>Every time zone the map reads local times in, the regional ones included.</summary>
    public static IEnumerable<Country> AllTimeZones => All.Concat(Regions.Select(region => region.Region));

    /// <summary>
    /// The country as it is at a postcode: the country itself, or the region of it in another time zone that the
    /// postcode lies in. A postcode that is missing or not one of the region's leaves the mainland.
    /// </summary>
    public static Country? At(Country? country, string? postcode)
    {
        if (country is null || string.IsNullOrWhiteSpace(postcode))
        {
            return country;
        }

        var trimmed = postcode.Trim();
        return Regions.FirstOrDefault(region => region.Code == country.Code && region.HasPostcode(trimmed)).Region ?? country;
    }

    /// <summary>Every way of writing a country the map reads: each name and each code.</summary>
    public static IEnumerable<string> Spellings => ByName.Keys;

    public static Country? Find(string? name) =>
        name is not null && ByName.TryGetValue(name.Trim(), out var country) ? country : null;
}
