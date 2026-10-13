using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Log.List;

public sealed record LogLine(
    int Sequence,
    DateTimeOffset At,
    string Actor,
    string Action,
    LogOutcome Outcome,
    string? CorrelationId,
    string? ShipperReference,
    string? TransportOrderNumber,
    string? Detail)
{
    internal static LogLine From(LogEntry entry) => new(
        entry.Sequence, entry.At, entry.Actor, entry.Action, entry.Outcome,
        entry.CorrelationId, entry.ShipperReference, entry.TransportOrderNumber, entry.Detail);
}
