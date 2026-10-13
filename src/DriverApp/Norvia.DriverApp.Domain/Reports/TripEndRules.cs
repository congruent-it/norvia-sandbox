namespace Norvia.DriverApp.Domain.Reports;

/// <summary>
/// When the Driver app lets a Driver end a Trip (P12): the TMS has recorded the start and every Stop has an outcome that the TMS has or hasn't answered yet. The
/// TMS decides all the same; this is the rule the Driver app can know.
/// </summary>
public static class TripEndRules
{
    public static TripEndDecision Decide(Trips.TripDetails details, IReadOnlyList<DriverReportRow> reports)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(reports);

        if (reports.FirstOrDefault(report => report.Kind == ReportKind.EndTrip && report.State is ReportState.Sending or ReportState.Recorded) is { } active)
        {
            return new TripEndDecision.Repeat(active.Id);
        }

        if (!TripProgress.IsStarted(reports))
        {
            return new TripEndDecision.NotStarted();
        }

        return TripProgress.Of(details, reports).Any(status => !status.Completed) ? new TripEndDecision.StopsOpen() : new TripEndDecision.Accept();
    }
}
