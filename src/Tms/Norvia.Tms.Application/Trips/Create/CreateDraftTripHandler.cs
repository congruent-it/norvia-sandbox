using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Create;

public sealed class CreateDraftTripHandler(IRepository<Trip> trips, ITripNumbers numbers, TripPlanningChecks checks)
    : ICommandHandler<CreateDraftTripCommand, Result<CreateDraftTripResult>>
{
    public async ValueTask<Result<CreateDraftTripResult>> Handle(CreateDraftTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var failures = await checks.CheckAsync(command.Day, command.VehicleId, command.DriverId, current: null, cancellationToken);
        if (failures.Count > 0)
        {
            return TripRefused.Because<CreateDraftTripResult>(failures);
        }

        var trip = Trip.CreateDraft(await numbers.NextAsync(cancellationToken), command.Day, command.VehicleId, command.DriverId);
        await trips.AddAsync(trip, cancellationToken);
        await trips.SaveChangesAsync(cancellationToken);
        return new CreateDraftTripResult(trip.Number.Value);
    }
}
