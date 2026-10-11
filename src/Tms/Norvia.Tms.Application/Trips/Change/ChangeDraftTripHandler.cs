using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Change;

public sealed class ChangeDraftTripHandler(IRepository<Trip> trips, TripPlanningChecks checks, TripShipments shipments, TripAmendments amendments)
    : ICommandHandler<ChangeDraftTripCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeDraftTripCommand command, CancellationToken cancellationToken)
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
        if (dispatched && MissingParts(command) is { Count: > 0 } missing)
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

        var parties = dispatched ? amendments.PartiesOf(command.VehicleId, command.DriverId) : null;
        if (dispatched && parties is null)
        {
            return TripRefused.Because(TripRefusal.VehicleOrDriverInactive(trip.Number));
        }

        var picked = trip.Pick(command.VehicleId, command.DriverId);
        var planned = trip.Plan(command.PlannedStart, command.PlannedDrivingDuration);
        if (parties is not null && (picked || planned))
        {
            await amendments.SendAsync(trip, parties, new CorrelationId(command.CorrelationId ?? CorrelationId.New().Value), cancellationToken);
        }

        await trips.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static List<string> MissingParts(ChangeDraftTripCommand command)
    {
        var missing = new List<string>();
        if (command.VehicleId is null)
        {
            missing.Add("a Vehicle");
        }

        if (command.DriverId is null)
        {
            missing.Add("a Driver");
        }

        if (command.PlannedStart is null)
        {
            missing.Add("a planned start");
        }

        if (command.PlannedDrivingDuration is null)
        {
            missing.Add("a planned driving duration");
        }

        return missing;
    }
}
