using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Change;

public sealed class ChangeDraftTripHandler(IRepository<Trip> trips, TripPlanningChecks checks) : ICommandHandler<ChangeDraftTripCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeDraftTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TripNumber.TryParse(command.Number, out var number)
            || await trips.FirstOrDefaultAsync(new TripByNumberSpec(number), cancellationToken) is not { } trip)
        {
            return TripRefused.Because(TripRefusal.TripNotFound(command.Number));
        }

        if (trip.EnsureDraft() is { } notDraft)
        {
            return TripRefused.Because(notDraft);
        }

        var failures = await checks.CheckAsync(trip.Day, command.VehicleId, command.DriverId, trip, cancellationToken);
        if (failures.Count > 0)
        {
            return TripRefused.Because(failures);
        }

        trip.Pick(command.VehicleId, command.DriverId);
        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
