using Ardalis.SharedKernel;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

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
            runId, sequence, at, LogEntry.PlannerActor, action, answer.Outcome, answer.CorrelationId,
            booking.Reference, transportOrderNumber, answer.ShipmentNumber, PlanningDetail(intent, answer)),
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

    private static string BookingDetail(PlannedBooking booking, BookingAnswer answer)
    {
        var intent = booking.Bad is { } bad ? $"Bad booking ({bad}), expected {booking.Expected}" : "Good booking";
        return answer.Detail is { } said ? $"{intent}. Norvia: {said}" : intent;
    }

    private static string? PlanningDetail(string? intent, PlannerAnswer answer) => (intent, answer.Detail) switch
    {
        (null, null) => null,
        (null, { } said) => $"TMS: {said}",
        ({ } meant, null) => meant,
        ({ } meant, { } said) => $"{meant}. TMS: {said}",
    };
}
