namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>The local time the Carrier formats carry: local to the location, without an offset, as their maps read it.</summary>
internal static class CarrierLocalTime
{
    // Europe is covered by three zones for the countries Norvia carries to; any other country reads as Central European.
    public static DateTime At(DateTimeOffset moment, string country) =>
        TimeZoneInfo.ConvertTime(moment, TimeZoneInfo.FindSystemTimeZoneById(ZoneOf(country))).DateTime;

    private static string ZoneOf(string country) => country switch
    {
        "GBR" or "IRL" => "Europe/London",
        "PRT" => "Europe/Lisbon",
        "FIN" or "EST" or "LVA" or "LTU" or "GRC" or "BGR" or "ROU" => "Europe/Helsinki",
        _ => "Europe/Amsterdam",
    };
}
