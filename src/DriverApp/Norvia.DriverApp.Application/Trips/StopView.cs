using Norvia.DriverApp.Application.Reports;

namespace Norvia.DriverApp.Application.Trips;

/// <summary>
/// One Stop of a Trip as the Driver works it: its details from the version the Driver app holds, what the Driver reported on it and what became of that,
/// and what the Driver can do now. The Stops come in the Planner's sequence, but the Driver completes them in any order (P12).
/// </summary>
/// <param name="Details">The Stop as the held version has it; null for a Stop the Driver completed whose Shipment the Planner took off in a later version.</param>
/// <param name="Outcome">The latest report that completes the Stop (Collected, Collection failed, Delivery failed), whatever became of it.</param>
/// <param name="TakenOff">The Driver completed the Stop, and a later version of the Trip no longer has its Shipment: the Driver calls the Planner.</param>
/// <param name="CanComplete">Whether to offer the reports that complete the Stop.</param>
/// <param name="Blocked">Why the Stop cannot be completed now, for the Driver; null when it can, or when it is done.</param>
/// <param name="Reports">Every report on the Stop, oldest first: its outcome, and any damage.</param>
public sealed record StopView(
    string ShipmentNumber,
    StopKindDto Kind,
    StopDto? Details,
    TripReportView? Outcome,
    bool TakenOff,
    bool CanComplete,
    bool CanReportDamage,
    string? Blocked,
    IReadOnlyList<TripReportView> Reports)
{
    /// <summary>The Driver gave the Stop an outcome that the TMS has, or hasn't answered yet.</summary>
    public bool Completed => Outcome is { State: ReportStateDto.Sending or ReportStateDto.Recorded };
}
