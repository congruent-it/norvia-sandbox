using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Start;

public sealed class StartTripHandler(IRepository<Trip> trips, PlanningHorizonRules horizon, TimeProvider time)
    : ICommandHandler<StartTripCommand, Result<StartTripResult>>
{
    public async ValueTask<Result<StartTripResult>> Handle(StartTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because<StartTripResult>(TripRefusal.TripNotFound(command.Number));
        }

        var now = time.GetUtcNow();
        if (trip.EnsureCanStart(new DriverId(command.DriverId), horizon.Today(now)) is { } refusal)
        {
            return TripRefused.Because<StartTripResult>(refusal);
        }

        var correlationId = new CorrelationId(command.CorrelationId);
        trip.Start(correlationId, now);

        // One save writes the status and TripStarted's outbox message together.
        await trips.SaveChangesAsync(cancellationToken);
        return new StartTripResult(trip.Number.Value, trip.Version, correlationId.Value);
    }
}
