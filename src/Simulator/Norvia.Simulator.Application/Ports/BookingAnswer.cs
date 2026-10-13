using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Ports;

/// <summary>Norvia's answer to a booking, as the log shows it. <see cref="Detail"/> holds a rejection's codes, never what was booked.</summary>
public sealed record BookingAnswer(LogOutcome Outcome, string? CorrelationId, string? TransportOrderNumber, string? Detail);
