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

        // Giving the Vehicle and Driver back is always allowed, also when the day has since left the Planning horizon.
        IReadOnlyList<TripAvailabilityFailure> failures = command.VehicleId is null && command.DriverId is null
            ? []
            : await checks.CheckAsync(trip.Day, command.VehicleId, command.DriverId, trip, cancellationToken);
        if (failures.Count > 0)
        {
            return TripRefused.Because(failures);
        }

        trip.Pick(command.VehicleId, command.DriverId);
        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
