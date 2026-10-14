using System.Collections.Concurrent;
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
/// window (Paced), and logs how Norvia answered each. Real paths stamp real time: nothing is backdated. Once the bookings are in,
/// the simulated Planner assigns the Transport Orders Norvia accepted.
/// </summary>
public sealed class ExecuteRunHandler(
    IRepository<SimulationRun> runs,
    IRepository<LogEntry> log,
    IPartnerApi partnerApi,
    ITmsApi tmsApi,
    IWorldSource world,
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

        using var writer = new RunLogWriter(log, run.Id, FirstSequenceAfterStart);
        var accepted = new ConcurrentBag<AcceptedOrder>();
        var stopped = await SendAllAsync(run, writer, accepted, command.StopSending, cancellationToken);
        stopped = stopped || await new PlannerStage(tmsApi, time).RunAsync(run, [.. accepted], world.Load(), writer, command.StopSending, cancellationToken);

        if (stopped)
        {
            await writer.NoteAsync(time.GetUtcNow(), "Run cancelled: nothing more is sent", cancellationToken);
            return Result.Success();
        }

        run.Complete(time.GetUtcNow());
        await runs.UpdateAsync(run, cancellationToken);
        await writer.NoteAsync(time.GetUtcNow(), Finished(run), cancellationToken);
        return Result.Success();
    }

    private async Task<bool> SendAllAsync(
        SimulationRun run, RunLogWriter writer, ConcurrentBag<AcceptedOrder> accepted, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var bookings = run.Plan.Bookings;
        var concurrency = Math.Max(1, limits.MaxConcurrentRequests);
        var spacing = run.Settings.Spacing(bookings.Count);
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
                inFlight.Add(SendAsync(bookings[index], writer, accepted, slots, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (stopSending.IsCancellationRequested)
        {
            // Cancelled: what is in flight lands below.
        }

        await Task.WhenAll(inFlight);
        return stopSending.IsCancellationRequested;
    }

    private static string Finished(SimulationRun run)
    {
        var sent = $"Run {run.Number} finished: {run.Plan.Bookings.Count} bookings sent";
        return run.Plan.Choices.Count == 0 ? sent : $"{sent}, {run.Plan.Choices.Count} Transport Orders for the Planner";
    }

    private async Task SendAsync(
        PlannedBooking booking, RunLogWriter writer, ConcurrentBag<AcceptedOrder> accepted, SemaphoreSlim slots, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await partnerApi.SendBookingAsync(booking, cancellationToken);
            await writer.BookingAsync(time.GetUtcNow(), booking, answer, cancellationToken);
            // Only an Accepted answer is a new Transport Order to assign: a repeat names the one that was assigned (or not) already.
            if (answer is { Outcome: LogOutcome.Accepted, TransportOrderNumber: { } number })
            {
                accepted.Add(new AcceptedOrder(booking, number));
            }
        }
        finally
        {
            slots.Release();
        }
    }
}
