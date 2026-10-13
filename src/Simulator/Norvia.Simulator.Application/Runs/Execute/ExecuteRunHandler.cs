using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// Sends each planned booking as its Shipper's Persona, as fast as the concurrency cap allows (Burst) or spread over the
/// window (Paced), and logs how Norvia answered each. Real paths stamp real time: nothing is backdated.
/// </summary>
public sealed class ExecuteRunHandler(
    IRepository<SimulationRun> runs,
    IRepository<LogEntry> log,
    IPartnerApi partnerApi,
    SimulationLimits limits,
    TimeProvider time)
    : ICommandHandler<ExecuteRunCommand, Result>
{
    // The run's first line, "Run started", is sequence 1.
    private const int FirstSequenceAfterStart = 2;

    public async ValueTask<Result> Handle(ExecuteRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var run = await runs.FirstOrDefaultAsync(new RunByIdSpec(command.RunId), cancellationToken);
        if (run is not { IsActive: true })
        {
            return Result.NotFound($"There is no active run {command.RunId.Value}.");
        }

        using var writer = new LogWriter(log, run.Id, FirstSequenceAfterStart);
        var stopped = await SendAllAsync(run, writer, command.StopSending, cancellationToken);

        if (stopped)
        {
            await writer.NoteAsync(time.GetUtcNow(), "Run cancelled: nothing more is sent", cancellationToken);
            return Result.Success();
        }

        run.Complete(time.GetUtcNow());
        await runs.UpdateAsync(run, cancellationToken);
        await writer.NoteAsync(time.GetUtcNow(), $"Run {run.Number} finished: {run.Plan.Bookings.Count} bookings sent", cancellationToken);
        return Result.Success();
    }

    private async Task<bool> SendAllAsync(SimulationRun run, LogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var bookings = run.Plan.Bookings;
        var concurrency = Math.Max(1, limits.MaxConcurrentRequests);
        var spacing = run.Settings.Mode == RunMode.Paced && bookings.Count > 0 ? run.Settings.Window / bookings.Count : TimeSpan.Zero;
        var began = time.GetUtcNow();
        using var slots = new SemaphoreSlim(concurrency);
        var inFlight = new List<Task>(bookings.Count);

        try
        {
            for (var index = 0; index < bookings.Count; index++)
            {
                var wait = began + (spacing * index) - time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, time, stopSending);
                }

                await slots.WaitAsync(stopSending);
                inFlight.Add(SendAsync(bookings[index], writer, slots, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (stopSending.IsCancellationRequested)
        {
            // Cancelled: what is in flight lands below.
        }

        await Task.WhenAll(inFlight);
        return stopSending.IsCancellationRequested;
    }

    private async Task SendAsync(PlannedBooking booking, LogWriter writer, SemaphoreSlim slots, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await partnerApi.SendBookingAsync(booking, cancellationToken);
            await writer.BookingAsync(time.GetUtcNow(), booking, answer, cancellationToken);
        }
        finally
        {
            slots.Release();
        }
    }

    /// <summary>One writer per run: the repository's context takes one write at a time, and the lines are numbered as they land.</summary>
    private sealed class LogWriter(IRepository<LogEntry> log, SimulationRunId runId, int next) : IDisposable
    {
        private readonly SemaphoreSlim _one = new(1);

        public void Dispose() => _one.Dispose();

        public Task BookingAsync(DateTimeOffset at, PlannedBooking booking, BookingAnswer answer, CancellationToken cancellationToken) =>
            AddAsync(sequence => LogEntry.Booking(
                runId, sequence, at, booking.PersonaName, "Send booking", answer.Outcome, answer.CorrelationId,
                booking.Reference, answer.TransportOrderNumber, Detail(booking, answer)),
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

        private static string Detail(PlannedBooking booking, BookingAnswer answer)
        {
            var intent = booking.Bad is { } bad ? $"Bad booking ({bad}), expected {booking.Expected}" : "Good booking";
            return answer.Detail is { } said ? $"{intent}. Norvia: {said}" : intent;
        }
    }
}
