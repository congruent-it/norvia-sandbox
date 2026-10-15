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

    /// <summary>Who plans the Transport Orders a run books: the simulated Planner.</summary>
    public const string PlannerActor = "Planner";

    /// <summary>What the database keeps of a line's detail.</summary>
    public const int MaxDetailLength = 500;

    private LogEntry(
        SimulationRunId runId, int sequence, DateTimeOffset at, string actor, string action, LogOutcome outcome,
        string? correlationId, string? shipperReference, string? transportOrderNumber, string? shipmentNumber, string? detail)
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
        ShipmentNumber = shipmentNumber;
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

    /// <summary>The Shipment the Planner's assignment planned, when the line is about one.</summary>
    public string? ShipmentNumber { get; private set; }

    /// <summary>What the plan intended and what Norvia said, such as the codes of a rejection.</summary>
    public string? Detail { get; private set; }

    public static LogEntry Booking(
        SimulationRunId runId, int sequence, DateTimeOffset at, string actor, string action, LogOutcome outcome,
        string? correlationId, string? shipperReference, string? transportOrderNumber, string? detail) =>
        new(runId, sequence, at, actor, action, outcome, correlationId, shipperReference, transportOrderNumber, null, detail);

    /// <summary>A call the Planner made to the TMS API: its answer's correlation ID and the keys it names.</summary>
    public static LogEntry Planning(
        SimulationRunId runId, int sequence, DateTimeOffset at, string action, LogOutcome outcome,
        string? correlationId, string? shipperReference, string? transportOrderNumber, string? shipmentNumber, string? detail) =>
        new(runId, sequence, at, PlannerActor, action, outcome, correlationId, shipperReference, transportOrderNumber, shipmentNumber, detail);

    /// <summary>A call a Carrier made to the Partner API as its Persona, on one Shipment: its answer's correlation ID and the keys it names.</summary>
    public static LogEntry Carrier(
        SimulationRunId runId, int sequence, DateTimeOffset at, string actor, string action, LogOutcome outcome,
        string? correlationId, string? shipperReference, string? transportOrderNumber, string? shipmentNumber, string? detail) =>
        new(runId, sequence, at, actor, action, outcome, correlationId, shipperReference, transportOrderNumber, shipmentNumber, detail);

    public static LogEntry Note(SimulationRunId runId, int sequence, DateTimeOffset at, string text) =>
        new(runId, sequence, at, SimulatorActor, text, LogOutcome.Info, null, null, null, null, null);
}
