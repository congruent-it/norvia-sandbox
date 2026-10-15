using System.Globalization;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>What the Carrier formats need to know of a country: its ISO 3166-1 alpha-3 code and the time zone its local times are read in (P5).</summary>
internal static class CarrierCountries
{
    private static readonly Dictionary<string, (string Alpha3, string Zone)> Known = new(StringComparer.Ordinal)
    {
        ["NL"] = ("NLD", "Europe/Amsterdam"),
        ["BE"] = ("BEL", "Europe/Brussels"),
        ["LU"] = ("LUX", "Europe/Luxembourg"),
        ["DE"] = ("DEU", "Europe/Berlin"),
    };

    public static string Alpha3(string alpha2) => Find(alpha2).Alpha3;

    /// <summary>The instant as local date and time at the country, without an offset, as the Carrier formats have it.</summary>
    public static string Local(DateTimeOffset at, string alpha2)
    {
        var local = TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(Find(alpha2).Zone)).DateTime;
        return local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static (string Alpha3, string Zone) Find(string alpha2) =>
        Known.TryGetValue(alpha2, out var country) ? country : throw new InvalidOperationException($"The Simulator doesn't know country '{alpha2}': it has no alpha-3 code or time zone for it.");
}
