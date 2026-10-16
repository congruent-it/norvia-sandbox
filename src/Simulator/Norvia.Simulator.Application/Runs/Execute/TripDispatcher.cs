using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Trips;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>Creates a Draft Trip and dispatches it through the TMS API, logging each call. What the TMS refuses is logged and the Trip goes no further.</summary>
internal sealed class TripDispatcher(ITmsApi tms, TimeProvider time)
{
    /// <summary>The number of the Trip, and whether it was dispatched; no number when the Draft wasn't created.</summary>
    public async Task<(string? Number, bool Dispatched)> DispatchAsync(TripDraft draft, string idempotencyKey, RunLogWriter writer, CancellationToken cancellationToken)
    {
        var created = await tms.CreateTripAsync(draft, idempotencyKey, cancellationToken);
        await writer.TripAsync(time.GetUtcNow(), TripActions.CreateTrip, created, created.TripNumber, Describe(draft), cancellationToken);
        if (created is not { Outcome: LogOutcome.TripBuilt, TripNumber: { } number })
        {
            return (null, false);
        }

        var sent = await tms.DispatchTripAsync(number, cancellationToken);
        await writer.TripAsync(time.GetUtcNow(), TripActions.DispatchTrip, sent, number, null, cancellationToken);
        return (number, sent.Outcome == LogOutcome.TripDispatched);
    }

    private static string Describe(TripDraft draft) =>
        $"{draft.Day:yyyy-MM-dd}, {draft.Vehicle.Registration} with {draft.Driver.Name}, {draft.Shipments.Count} {(draft.Shipments.Count == 1 ? "Shipment" : "Shipments")}, start {draft.PlannedStart:HH:mm}, {draft.PlannedDrivingDuration.TotalMinutes:0} minutes driving";
}
