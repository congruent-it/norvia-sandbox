using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Change;

public sealed class ChangeDraftTripHandler(IRepository<Trip> trips, TripPlanningChecks checks) : ICommandHandler<ChangeDraftTripCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeDraftTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because(TripRefusal.TripNotFound(command.Number));
        }

        if (trip.EnsureDraft() is { } notDraft)
        {
            return TripRefused.Because(notDraft);
        }

        // Giving everything back is always allowed, also when the day has since left the Planning horizon.
        var giveBack = command.VehicleId is null && command.DriverId is null && command.PlannedStart is null && command.PlannedDrivingDuration is null;
        var check = giveBack
            ? TripPlanningCheck.Passed
            : await checks.CheckAsync(trip.Day, command.VehicleId, command.DriverId, command.PlannedStart, command.PlannedDrivingDuration, trip, cancellationToken);
        if (!check.IsOk)
        {
            return TripRefused.Because(check);
        }

        trip.Pick(command.VehicleId, command.DriverId);
        trip.Plan(command.PlannedStart, command.PlannedDrivingDuration);
        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
