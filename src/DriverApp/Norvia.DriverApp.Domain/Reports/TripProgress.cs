using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>
/// What the Driver app knows of a Trip's Stops: the Stops of the version it holds, in the Planner's sequence, with the reports the Driver made on each, then the
/// Stops the Driver completed whose Shipment a later version took off (P12). A report belongs to the Trip, not to a version, so a new version keeps the outcomes
/// and report states of the Stops already completed.
/// </summary>
public static class TripProgress
{
    /// <summary>The TMS has said the Trip started.</summary>
    public static bool IsStarted(IEnumerable<DriverReportRow> reports) =>
        reports.Any(report => report.Kind == ReportKind.StartTrip && report.State == ReportState.Recorded);

    public static IReadOnlyList<StopStatus> Of(TripDetails details, IReadOnlyList<DriverReportRow> reports)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(reports);

        var started = IsStarted(reports);
        var stopReports = reports.Where(report => report.ShipmentNumber is not null && report.Stop is not null).ToList();
        var current = details.Stops.OrderBy(stop => stop.Sequence).ToList();
        var statuses = new List<StopStatus>();

        foreach (var stop in current)
        {
            var reference = new StopRef(stop.ShipmentNumber, stop.Kind);
            var own = ReportsOn(stopReports, reference);
            var outcome = OutcomeOf(own);
            var completed = outcome is { State: ReportState.Sending or ReportState.Recorded };
            statuses.Add(new StopStatus(reference, stop, own, outcome, false, completed ? StopBlock.None : BlockOf(stop, current, stopReports, started), started));
        }

        // A completed Stop whose Shipment is no longer on the Trip is kept, not silently removed.
        foreach (var orphan in stopReports
            .Where(report => report.CompletesStop && !current.Any(stop => stop.ShipmentNumber == report.ShipmentNumber && stop.Kind == report.Stop))
            .GroupBy(report => new StopRef(report.ShipmentNumber!, report.Stop!.Value))
            .OrderBy(group => group.Min(report => report.EventTime)))
        {
            var own = ReportsOn(stopReports, orphan.Key);
            statuses.Add(new StopStatus(orphan.Key, null, own, OutcomeOf(own), true, StopBlock.None, false));
        }

        return statuses;
    }

    private static List<DriverReportRow> ReportsOn(IEnumerable<DriverReportRow> stopReports, StopRef stop) =>
        [.. stopReports.Where(report => report.ShipmentNumber == stop.ShipmentNumber && report.Stop == stop.Kind).OrderBy(report => report.EventTime)];

    private static DriverReportRow? OutcomeOf(IEnumerable<DriverReportRow> own) =>
        own.Where(report => report.CompletesStop).OrderByDescending(report => report.EventTime).FirstOrDefault();

    /// <summary>A delivery waits for its collection, unless the Shipment is a Re-delivery and has only its delivery Stop on this version.</summary>
    private static StopBlock BlockOf(TripStop stop, List<TripStop> current, List<DriverReportRow> stopReports, bool started)
    {
        if (!started)
        {
            return StopBlock.TripNotStarted;
        }

        if (stop.Kind == StopKind.Collection || !current.Any(other => other.ShipmentNumber == stop.ShipmentNumber && other.Kind == StopKind.Collection))
        {
            return StopBlock.None;
        }

        return OutcomeOf(ReportsOn(stopReports, new StopRef(stop.ShipmentNumber, StopKind.Collection))) switch
        {
            { Kind: ReportKind.Collected, State: ReportState.Sending or ReportState.Recorded } => StopBlock.None,
            { Kind: ReportKind.CollectionFailed, State: ReportState.Sending or ReportState.Recorded } => StopBlock.CollectionFailed,
            _ => StopBlock.CollectFirst,
        };
    }
}
