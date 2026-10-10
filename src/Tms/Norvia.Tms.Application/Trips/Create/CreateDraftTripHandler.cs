using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Create;

public sealed class CreateDraftTripHandler(IRepository<Trip> trips, ITripNumbers numbers, TripPlanningChecks checks, TripShipments shipments)
    : ICommandHandler<CreateDraftTripCommand, Result<CreateDraftTripResult>>
{
    public async ValueTask<Result<CreateDraftTripResult>> Handle(CreateDraftTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FirstOrDefaultAsync(new TripByIdempotencyKeySpec(command.IdempotencyKey), cancellationToken) is { } created)
        {
            return new CreateDraftTripResult(created.Number.Value);
        }

        var (added, notFound) = await shipments.FindAsync(command.ShipmentNumbers ?? [], cancellationToken);
        var check = await checks.CheckAsync(
            command.Day, command.VehicleId, command.DriverId, command.PlannedStart, command.PlannedDrivingDuration, current: null, added, added, cancellationToken);
        if (notFound.Count > 0 || !check.IsOk)
        {
            return TripRefused.Because<CreateDraftTripResult>(check with { Refusals = [.. notFound, .. check.Refusals] });
        }

        var trip = Trip.CreateDraft(await numbers.NextAsync(cancellationToken), command.Day, command.VehicleId, command.DriverId, command.IdempotencyKey);
        trip.Plan(command.PlannedStart, command.PlannedDrivingDuration);
        foreach (var item in added)
        {
            trip.AddShipment(item.Shipment.Id);
        }

        await trips.AddAsync(trip, cancellationToken);
        await trips.SaveChangesAsync(cancellationToken);
        return new CreateDraftTripResult(trip.Number.Value, check.Warnings);
    }
}
