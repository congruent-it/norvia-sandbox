using Ardalis.SharedKernel;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>One writer per run: the repository's context takes one write at a time, and the lines are numbered as they land.</summary>
internal sealed class RunLogWriter(IRepository<LogEntry> log, SimulationRunId runId, int next) : IDisposable
{
    private readonly SemaphoreSlim _one = new(1);

    public void Dispose() => _one.Dispose();

    public Task BookingAsync(DateTimeOffset at, PlannedBooking booking, BookingAnswer answer, CancellationToken cancellationToken) =>
        AddAsync(sequence => LogEntry.Booking(
            runId, sequence, at, booking.PersonaName, "Send booking", answer.Outcome, answer.CorrelationId,
            booking.Reference, answer.TransportOrderNumber, BookingDetail(booking, answer)),
            cancellationToken);

    public Task PlanningAsync(
        DateTimeOffset at, string action, PlannedBooking booking, string transportOrderNumber, PlannerAnswer answer, string? intent,
        CancellationToken cancellationToken) =>
        AddAsync(sequence => LogEntry.Planning(
            runId, sequence, at, action, answer.Outcome, answer.CorrelationId,
            booking.Reference, transportOrderNumber, answer.ShipmentNumber, Detail(intent, answer.Detail, "TMS")),
            cancellationToken);

    /// <summary>A call the Planner made on a Trip: it holds several Shipments, so the line names the Trip and not one of them.</summary>
    public Task TripAsync(DateTimeOffset at, string action, PlannerAnswer answer, string? tripNumber, string? detail, CancellationToken cancellationToken) =>
        AddAsync(sequence => LogEntry.Planning(
            runId, sequence, at, action, answer.Outcome, answer.CorrelationId,
            null, null, null, Detail(detail, answer.Detail, "TMS"), tripNumber),
            cancellationToken);

    /// <summary>A call a Driver made to the Driver app as themselves, on a Trip and often on one Shipment of it.</summary>
    public Task DriverAsync(
        DateTimeOffset at, SimulatedDriver driver, string tripNumber, OwnFleetShipment? shipment, string action, DriverCallAnswer answer, string? detail,
        CancellationToken cancellationToken) =>
        AddAsync(sequence => LogEntry.Driver(
            runId, sequence, at, driver.Name, action, answer.Outcome, answer.CorrelationId,
            shipment?.Booking.Reference, shipment?.TransportOrderNumber, shipment?.ShipmentNumber, tripNumber, Detail(detail, answer.Detail, "Driver app")),
            cancellationToken);

    /// <summary>A call a Carrier made as its Persona on one of the Shipments it carries.</summary>
    public Task CarrierAsync(DateTimeOffset at, PlannedShipment shipment, string action, CarrierCallAnswer answer, string? detail, CancellationToken cancellationToken) =>
        AddAsync(sequence => LogEntry.Carrier(
            runId, sequence, at, ActorOf(shipment), action, answer.Outcome, answer.CorrelationId,
            shipment.Booking.Reference, shipment.TransportOrderNumber, shipment.ShipmentNumber, Detail(detail, answer.Detail, "Norvia")),
            cancellationToken);

    /// <summary>A Carrier that was asked and said nothing: no call, so no correlation ID.</summary>
    public Task SilenceAsync(DateTimeOffset at, PlannedShipment shipment, string action, string? detail, CancellationToken cancellationToken) =>
        AddAsync(sequence => LogEntry.Carrier(
            runId, sequence, at, ActorOf(shipment), action, LogOutcome.NoAnswer, null,
            shipment.Booking.Reference, shipment.TransportOrderNumber, shipment.ShipmentNumber, detail),
            cancellationToken);

    public Task NoteAsync(DateTimeOffset at, string text, CancellationToken cancellationToken) =>
        AddAsync(sequence => LogEntry.Note(runId, sequence, at, text), cancellationToken);

    private async Task AddAsync(Func<int, LogEntry> entry, CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken);
        try
        {
            await log.AddAsync(entry(next++), cancellationToken);
        }
        finally
        {
            _one.Release();
        }
    }

    private static string ActorOf(PlannedShipment shipment) => shipment.Carrier.PersonaName ?? shipment.Carrier.Name;

    // What the actor meant, then what the system on the other side said.
    private static string? Detail(string? meant, string? said, string system) => (meant, said) switch
    {
        (null, null) => null,
        (null, { } reply) => $"{system}: {reply}",
        ({ } intent, null) => intent,
        ({ } intent, { } reply) => $"{intent}. {system}: {reply}",
    };

    private static string BookingDetail(PlannedBooking booking, BookingAnswer answer)
    {
        var intent = booking.Bad is { } bad ? $"Bad booking ({bad}), expected {booking.Expected}" : "Good booking";
        return answer.Detail is { } said ? $"{intent}. Norvia: {said}" : intent;
    }

}
