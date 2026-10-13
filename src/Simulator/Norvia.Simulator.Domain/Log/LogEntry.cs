using Ardalis.SharedKernel;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Log;

/// <summary>
/// One line of a run's log: what an actor did, how Norvia answered, and the keys that find its Message trail in the
/// Integration console. It never holds what was booked, which has personal data in it (coding guidelines §7).
/// </summary>
public sealed class LogEntry : EntityBase<LogEntryId>, IAggregateRoot
{
    public const string SimulatorActor = "Simulator";

    /// <summary>What the database keeps of a line's detail.</summary>
    public const int MaxDetailLength = 500;

    private LogEntry(
        SimulationRunId runId, int sequence, DateTimeOffset at, string actor, string action, LogOutcome outcome,
        string? correlationId, string? shipperReference, string? transportOrderNumber, string? detail)
    {
        Id = LogEntryId.New();
        RunId = runId;
        Sequence = sequence;
        At = at;
        Actor = actor;
        Action = action;
        Outcome = outcome;
        CorrelationId = correlationId;
        ShipperReference = shipperReference;
        TransportOrderNumber = transportOrderNumber;
        Detail = detail is { Length: > MaxDetailLength } ? detail[..MaxDetailLength] : detail;
    }

    public SimulationRunId RunId { get; private set; }

    /// <summary>The line's place in its run, from 1.</summary>
    public int Sequence { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary>Who acted: the Persona's name, or <see cref="SimulatorActor"/>.</summary>
    public string Actor { get; private set; }

    public string Action { get; private set; }

    public LogOutcome Outcome { get; private set; }

    /// <summary>The <c>X-Correlation-ID</c> Norvia answered with, by which the Integration console finds the Message trail.</summary>
    public string? CorrelationId { get; private set; }

    public string? ShipperReference { get; private set; }

    public string? TransportOrderNumber { get; private set; }

    /// <summary>What the plan intended and what Norvia said, such as the codes of a rejection.</summary>
    public string? Detail { get; private set; }

    public static LogEntry Booking(
        SimulationRunId runId, int sequence, DateTimeOffset at, string actor, string action, LogOutcome outcome,
        string? correlationId, string? shipperReference, string? transportOrderNumber, string? detail) =>
        new(runId, sequence, at, actor, action, outcome, correlationId, shipperReference, transportOrderNumber, detail);

    public static LogEntry Note(SimulationRunId runId, int sequence, DateTimeOffset at, string text) =>
        new(runId, sequence, at, SimulatorActor, text, LogOutcome.Info, null, null, null, null);
}
