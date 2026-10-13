using System.Globalization;
using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Application.Reports.Deliver;
using Norvia.DriverApp.Application.Reports.ReportStop;
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

    public static string ReportAction(ReportKindDto kind) => kind switch
    {
        ReportKindDto.StartTrip => "Start Trip",
        ReportKindDto.Collected => "Collected",
        ReportKindDto.CollectionFailed => "Collection failed",
        ReportKindDto.DeliveryFailed => "Delivery failed",
        ReportKindDto.Damaged => "Damage reported",
        ReportKindDto.Delivered => "Delivered",
        ReportKindDto.ProofOfDelivery => "Proof of delivery",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A kind of report the Driver app has no words for."),
    };

    public static string ReportState(ReportStateDto state) => state switch
    {
        ReportStateDto.Sending => "Sending",
        ReportStateDto.Recorded => "Recorded",
        ReportStateDto.Refused => "Refused",
        ReportStateDto.NotRecorded => "Not recorded",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A state of a report the Driver app has no words for."),
    };

    public static string StopAction(StopKindDto kind) => kind == StopKindDto.Collection ? "Collect" : "Deliver";

    /// <summary>What the Driver reads on a Stop of what became of their report on it.</summary>
    public static string StopStatus(StopView stop) => stop switch
    {
        { TakenOff: true, Outcome: not { State: ReportStateDto.Refused } } => TakenOffByThePlanner,
        { Outcome: null } => "Open",
        { Outcome: { State: ReportStateDto.Refused } } => "Refused",
        { Outcome: { State: ReportStateDto.NotRecorded } } => "Not recorded",
        { Outcome: { } outcome } => ReportAction(outcome.Kind),
    };

    public const string TakenOffByThePlanner = "Taken off by the Planner, call the Planner";

    public static string Problem(string? identifier) => identifier switch
    {
        "remark" => $"Say what happened: a remark is required, up to {ReportStopCommand.MaximumRemarkLength} characters, and with reservations the remark says which.",
        "receivedBy" => $"Say who received the goods, up to {DeliverCommand.MaximumReceivedByLength} characters.",
        "condition" => "Say whether the goods were received clean or with reservations.",
        "signature" => "The Consignee has to sign: draw the signature before you deliver.",
        "photos" => $"Photos are at most {DeliverCommand.MaximumPhotos} JPEG or PNG files of at most {DeliverCommand.MaximumFileBytes / (1024 * 1024)} MB each.",
        "address" => "This Stop has no complete address to report from. Call the Planner.",
        _ => "That report couldn't be made.",
    };

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
