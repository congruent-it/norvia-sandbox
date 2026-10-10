using Ardalis.Result;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

internal static class TripDeliveryReports
{
    /// <summary>The answer to a report once the Trip has taken it: what the version is now, or why the report is refused.</summary>
    public static Result<TripDeliveryReportResult> Answer(Trip trip, int version, TripDeliveryReport report) =>
        report == TripDeliveryReport.UnknownVersion
            ? TripRefused.Because<TripDeliveryReportResult>(TripRefusal.TripVersionNotFound(trip.Number, version))
            : new TripDeliveryReportResult(trip.Number.Value, version, trip.Deliveries.Single(delivery => delivery.Version == version).State, report == TripDeliveryReport.Ignored);
}
