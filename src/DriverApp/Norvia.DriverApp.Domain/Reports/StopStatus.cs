using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.Reports;

/// <summary>What the Driver app knows of one Stop of a Trip: its details, the reports on it, its outcome and whether the Driver can report on it now.</summary>
/// <param name="Details">The Stop as the held version has it; null for a Stop the Driver completed whose Shipment a later version took off.</param>
/// <param name="Reports">Every report on the Stop, oldest first.</param>
/// <param name="Outcome">The latest report that completes the Stop, whatever became of it.</param>
/// <param name="TakenOff">The Driver completed the Stop, and a later version of the Trip no longer has its Shipment.</param>
public sealed record StopStatus(StopRef Stop, TripStop? Details, IReadOnlyList<DriverReportRow> Reports, DriverReportRow? Outcome, bool TakenOff, StopBlock Block, bool CanReportDamage)
{
    /// <summary>The Driver gave the Stop an outcome that the TMS has, or hasn't answered yet.</summary>
    public bool Completed => Outcome is { State: ReportState.Sending or ReportState.Recorded };

    public bool CanComplete => !TakenOff && !Completed && Block == StopBlock.None;
}
