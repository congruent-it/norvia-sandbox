namespace Norvia.Simulator.Domain.Log;

/// <summary>A log line as the screens read it: what happened and the keys that find its Message trail.</summary>
public sealed record LogLine(
    int Sequence,
    DateTimeOffset At,
    string Actor,
    string Action,
    LogOutcome Outcome,
    string? CorrelationId,
    string? ShipperReference,
    string? TransportOrderNumber,
    string? ShipmentNumber,
    string? Detail);
