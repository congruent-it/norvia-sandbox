using System.Globalization;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.Plans;

/// <summary>
/// The addresses, names and phone numbers a Simulation plan is made of: a pool of streets for the Benelux, and for the
/// German addresses of bookings that find no Lane. A postcode is made from its Zone's prefix, so it lies in the Zone. All
/// of it is fictional.
/// </summary>
internal static class AddressPool
{
    private static readonly Dictionary<string, string[]> Streets = new(StringComparer.Ordinal)
    {
        ["NL"] = ["Kerkstraat", "Industrieweg", "Havenstraat", "Stationsplein", "Molenlaan", "Handelskade", "Dorpsstraat", "Nijverheidsweg", "Bergweg", "Wilhelminalaan"],
        ["BE"] = ["Nijverheidslaan", "Stationsstraat", "Havenlaan", "Kerkstraat", "Molenstraat", "Handelsstraat", "Noorderlaan", "Boulevard Industriel"],
        ["LU"] = ["Rue de l'Industrie", "Route d'Arlon", "Avenue de la Gare", "Rue du Commerce", "Zone Industrielle"],
    };

    private static readonly PlannedAddress[] GermanAddresses =
    [
        new("Hansaallee 210", "40549", "Düsseldorf", "DE"),
        new("Kölner Landstraße 301", "40591", "Düsseldorf", "DE"),
        new("Hohenzollernring 54", "50672", "Köln", "DE"),
        new("Alfredstraße 12", "45130", "Essen", "DE"),
        new("Königstraße 77", "47051", "Duisburg", "DE"),
    ];

    private static readonly string[] BusinessKinds =
        ["Groothandel", "Horeca", "Supermarkt", "Tuincentrum", "Bouwmarkt", "Slijterij", "Bakkerij", "Distributie", "Webshop", "Cateraar"];

    private static readonly string[] BusinessNames =
        ["De Linde", "Vermeulen", "Janssens", "De Wit", "Peeters", "Van Dam", "Schmit", "Bakker", "Claes", "Smeets", "Hoekstra", "Weber"];

    private static readonly string[] FirstNames = ["Sanne", "Pieter", "Joris", "Femke", "Luc", "Marie", "Daan", "Els", "Thijs", "Anouk"];

    private static readonly string[] LastNames = ["de Vries", "Janssens", "Bakker", "Peeters", "Schmit", "Visser", "Maes", "Smit"];

    public static PlannedAddress InZone(RateZone zone, SeededRandom random)
    {
        var streets = Streets.GetValueOrDefault(zone.Country) ?? Streets["NL"];
        var street = $"{random.Pick(streets)} {random.Between(1, 180)}";
        return new PlannedAddress(street, Postcode(zone, random), zone.Name, zone.Country);
    }

    public static PlannedAddress Germany(SeededRandom random) => random.Pick(GermanAddresses);

    public static string Consignee(string city, SeededRandom random) =>
        $"{random.Pick(BusinessKinds)} {random.Pick(BusinessNames)} {city}";

    public static string Contact(SeededRandom random) => $"{random.Pick(FirstNames)} {random.Pick(LastNames)}";

    public static string Phone(string country, SeededRandom random)
    {
        var (code, area) = country switch
        {
            "BE" => ("+32", "3"),
            "LU" => ("+352", "27"),
            _ => ("+31", "10"),
        };
        return string.Create(CultureInfo.InvariantCulture, $"{code} {area} 555 {random.Between(1000, 9999)}");
    }

    // Four digits starting with the Zone's prefix; Dutch postcodes add two letters. Compared without spaces by the TMS (P2).
    private static string Postcode(RateZone zone, SeededRandom random)
    {
        var digits = random.Pick(zone.PostcodePrefixes);
        while (digits.Length < 4)
        {
            digits += random.Next(10).ToString(CultureInfo.InvariantCulture);
        }

        return zone.Country == "NL" ? $"{digits} {(char)('A' + random.Next(26))}{(char)('A' + random.Next(26))}" : digits;
    }
}
