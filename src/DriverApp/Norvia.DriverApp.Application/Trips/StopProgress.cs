using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips;

/// <summary>
/// What the Driver app knows of a Trip's Stops: the Stops of the version it holds, in the Planner's sequence, with the reports the Driver made on each, then the
/// Stops the Driver completed whose Shipment a later version took off (P12). A report belongs to the Trip, not to a version, so a new version keeps the outcomes
/// and report states of the Stops already completed.
/// </summary>
internal static class StopProgress
{
    public const string TripNotStarted = "Start the Trip first.";
    public const string CollectFirst = "Collect the Shipment first.";
    public const string CollectionFailed = "The collection failed, so there is nothing to deliver.";

    public static bool IsStarted(IEnumerable<DriverReportRow> reports) =>
        reports.Any(report => report.Kind == ReportKind.StartTrip && report.State == ReportState.Recorded);

    public static IReadOnlyList<StopView> Of(TripDetails details, IReadOnlyList<DriverReportRow> reports)
    {
        var started = IsStarted(reports);
        var stopReports = reports.Where(report => report.ShipmentNumber is not null && report.Stop is not null).ToList();
        var current = details.Stops.OrderBy(stop => stop.Sequence).ToList();
        var views = new List<StopView>();

        foreach (var stop in current)
        {
            var own = ReportsOn(stopReports, stop.ShipmentNumber, stop.Kind);
            var outcome = OutcomeOf(own);
            var completed = outcome is { State: ReportStateDto.Sending or ReportStateDto.Recorded };
            var blocked = completed ? null : BlockedReason(stop, current, stopReports, started);
            views.Add(new StopView(
                stop.ShipmentNumber, stop.Kind.ToDto(), stop.ToDto(), outcome, false, !completed && blocked is null, started, blocked, [.. own.Select(report => report.ToView())]));
        }

        // A completed Stop whose Shipment is no longer on the Trip is kept, not silently removed.
        foreach (var orphan in stopReports
            .Where(report => report.GivesStopAnOutcome && !current.Any(stop => stop.ShipmentNumber == report.ShipmentNumber && stop.Kind == report.Stop))
            .GroupBy(report => (Shipment: report.ShipmentNumber!, Stop: report.Stop!.Value))
            .OrderBy(group => group.Min(report => report.EventTime)))
        {
            var own = ReportsOn(stopReports, orphan.Key.Shipment, orphan.Key.Stop);
            views.Add(new StopView(orphan.Key.Shipment, orphan.Key.Stop.ToDto(), null, OutcomeOf(own), true, false, false, null, [.. own.Select(report => report.ToView())]));
        }

        return views;
    }

    private static List<DriverReportRow> ReportsOn(IEnumerable<DriverReportRow> stopReports, string shipmentNumber, StopKind kind) =>
        [.. stopReports.Where(report => report.ShipmentNumber == shipmentNumber && report.Stop == kind).OrderBy(report => report.EventTime)];

    private static TripReportView? OutcomeOf(IEnumerable<DriverReportRow> own) =>
        own.Where(report => report.GivesStopAnOutcome).OrderByDescending(report => report.EventTime).Select(report => report.ToView()).FirstOrDefault();

    /// <summary>A delivery waits for its collection, unless the Shipment is a Re-delivery and has only its delivery Stop on this version.</summary>
    private static string? BlockedReason(TripStop stop, List<TripStop> current, List<DriverReportRow> stopReports, bool started)
    {
        if (!started)
        {
            return TripNotStarted;
        }

        if (stop.Kind == StopKind.Collection || !current.Any(other => other.ShipmentNumber == stop.ShipmentNumber && other.Kind == StopKind.Collection))
        {
            return null;
        }

        var collection = OutcomeOf(ReportsOn(stopReports, stop.ShipmentNumber, StopKind.Collection));
        return collection switch
        {
            { Kind: ReportKindDto.Collected, State: ReportStateDto.Sending or ReportStateDto.Recorded } => null,
            { Kind: ReportKindDto.CollectionFailed, State: ReportStateDto.Sending or ReportStateDto.Recorded } => CollectionFailed,
            _ => CollectFirst,
        };
    }
}
