using System.Globalization;
using Norvia.DriverApp.Application.Trips;

namespace Norvia.DriverApp.Web;

/// <summary>How the Driver, a Dutch Driver on Dutch roads, reads a Trip: times in Norvia's time zone (Rotterdam's), whatever the server's is.</summary>
internal static class TripText
{
    private static readonly TimeZoneInfo NorviaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static string Day(DateOnly day) => day.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);

    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan duration) => $"{(int)duration.TotalHours} h {duration.Minutes:00} min";

    public static string VehicleType(string type) => type switch
    {
        "RigidTruck" => "rigid truck",
        "TractorWithTrailer" => "tractor with trailer",
        _ => type.ToLowerInvariant(),
    };

    public static string StopAction(StopKindDto kind) => kind == StopKindDto.Collection ? "Collect" : "Deliver";

    public static string Address(StopAddressDto? address) =>
        address is null ? "No address" : string.Join(", ", new[] { address.Street, $"{address.Postcode} {address.City}".Trim(), address.Country }.Where(part => !string.IsNullOrWhiteSpace(part)));

    public static string Window(StopWindowDto? window)
    {
        if (window is not { Earliest: { } earliest, Latest: { } latest })
        {
            return "No window";
        }

        var from = TimeZoneInfo.ConvertTime(earliest, NorviaTimeZone);
        var to = TimeZoneInfo.ConvertTime(latest, NorviaTimeZone);
        return $"{from.ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture)} to {to.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }

    public static string GoodsLine(StopGoodsLineDto line) =>
        string.Join(", ", new[] { line.Quantity is { } quantity ? $"{quantity} × {line.PackageType}" : line.PackageType, line.WeightKg is { } weight ? $"{weight:0.#} kg" : null }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
