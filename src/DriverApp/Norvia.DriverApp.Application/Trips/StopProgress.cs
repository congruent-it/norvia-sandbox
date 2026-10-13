using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.Trips;

/// <summary>What the Driver sees of a Trip's Stops: the Domain's <see cref="TripProgress"/>, in the DTOs the Application's callers use.</summary>
internal static class StopProgress
{
    public static IReadOnlyList<StopView> Of(TripDetails details, IReadOnlyList<DriverReportRow> reports) =>
        [.. TripProgress.Of(details, reports).Select(ToView)];

    public static StopView ToView(this StopStatus status) => new(
        status.Stop.ShipmentNumber,
        status.Stop.Kind.ToDto(),
        status.Details?.ToDto(),
        status.Outcome?.ToView(),
        status.TakenOff,
        status.CanComplete,
        status.CanReportDamage,
        StopBlocks.Text(status.Block),
        [.. status.Reports.Select(report => report.ToView())]);
}
