using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The TMS's answer to one of the Planner's calls, as the log shows it. <see cref="Detail"/> holds a refusal's code, never the
/// message the TMS words it with, which may quote what was booked.
/// </summary>
public sealed record PlannerAnswer(LogOutcome Outcome, string? CorrelationId, string? ShipmentNumber, string? Detail);
