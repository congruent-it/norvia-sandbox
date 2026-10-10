using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

internal static class TripDeliveryReports
{
    /// <summary>
    /// Finds the Trip, lets it take the report, and saves when the report changed something. A late or repeated report is answered as
    /// ignored, and a Trip or version that doesn't exist is refused.
    /// </summary>
    public static async ValueTask<Result<TripDeliveryReportResult>> HandleAsync(
        IRepository<Trip> trips, string number, int version, Func<Trip, TripDeliveryReport> report, CancellationToken cancellationToken)
    {
        if (await trips.FindAsync(number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because<TripDeliveryReportResult>(TripRefusal.TripNotFound(number));
        }

        var outcome = report(trip);
        if (outcome == TripDeliveryReport.Recorded)
        {
            await trips.SaveChangesAsync(cancellationToken);
        }

        return outcome == TripDeliveryReport.UnknownVersion
            ? TripRefused.Because<TripDeliveryReportResult>(TripRefusal.TripVersionNotFound(trip.Number, version))
            : new TripDeliveryReportResult(trip.Number.Value, version, trip.DeliveryOf(version)!.State, outcome == TripDeliveryReport.Ignored);
    }
}
