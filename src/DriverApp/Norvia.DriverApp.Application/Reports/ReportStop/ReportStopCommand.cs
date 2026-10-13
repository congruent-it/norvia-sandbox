using Ardalis.Result;
using Mediator;
using Norvia.DriverApp.Application.Trips;

namespace Norvia.DriverApp.Application.Reports.ReportStop;

/// <summary>The Driver reports on a Stop of a Trip: Collected, Collection failed or Delivery failed completes it, Damaged doesn't (P12).</summary>
/// <param name="DriverId">The Driver ID from the Driver's token, never from the request (ADR 0005, ADR 0037).</param>
/// <param name="Remark">What the Driver says. Required for a failure and for damage.</param>
public sealed record ReportStopCommand(string TripNumber, string DriverId, string ShipmentNumber, StopKindDto Stop, StopActionDto Action, string? Remark)
    : ICommand<Result<ReportStopResult>>;
