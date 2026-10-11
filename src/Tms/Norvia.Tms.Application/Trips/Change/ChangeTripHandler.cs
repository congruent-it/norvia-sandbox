using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Change;

public sealed class ChangeTripHandler(IRepository<Trip> trips, TripPlanningChecks checks, TripShipments shipments, TripAmendments amendments)
    : ICommandHandler<ChangeTripCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because(TripRefusal.TripNotFound(command.Number));
        }

        if (trip.EnsureAmendable() is { } underWay)
        {
            return TripRefused.Because(underWay);
        }

        // A Dispatched Trip stays complete: an Amend can change a part, not take it away.
        var dispatched = trip.Status == TripStatus.Dispatched;
        if (dispatched && command.MissingParts() is { Count: > 0 } missing)
        {
            return Result.Invalid(
                [.. missing.Select(part => TripRefusal.TripIncomplete(trip.Number, part)).Select(refusal => new ValidationError("Trip", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error))]);
        }

        // Giving everything back is always allowed on a Draft, also when the day has since left the Planning horizon.
        var check = command.GivesEverythingBack && !dispatched
            ? TripPlanningCheck.Passed
            : await checks.CheckAsync(
                trip.Day, command.VehicleId, command.DriverId, command.PlannedStart, command.PlannedDrivingDuration, trip, await shipments.OfAsync([.. trip.ShipmentIds], cancellationToken), [], cancellationToken);
        if (!check.IsOk)
        {
            return TripRefused.Because(check);
        }

        var (parties, inactive) = amendments.Prepare(trip, command.VehicleId, command.DriverId);
        if (inactive is not null)
        {
            return TripRefused.Because(inactive);
        }

        var picked = trip.Pick(command.VehicleId, command.DriverId);
        var planned = trip.Plan(command.PlannedStart, command.PlannedDrivingDuration);
        if (picked || planned)
        {
            await amendments.SendAsync(trip, parties, command.CorrelationId, cancellationToken);
        }

        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
