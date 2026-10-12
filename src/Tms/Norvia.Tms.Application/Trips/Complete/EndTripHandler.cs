using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Complete;

public sealed class EndTripHandler(IRepository<Trip> trips, IRepository<Shipment> shipments, TripOutcomes tripOutcomes, TimeProvider time)
    : ICommandHandler<EndTripCommand, Result<EndTripResult>>
{
    public async ValueTask<Result<EndTripResult>> Handle(EndTripCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await trips.FindAsync(command.Number, cancellationToken) is not { } trip)
        {
            return TripRefused.Because<EndTripResult>(TripRefusal.TripNotFound(command.Number));
        }

        var outcomes = (await tripOutcomes.OfAsync([trip.Number], cancellationToken)).GetValueOrDefault(trip.Number) ?? StopOutcomes.From([]);
        if (trip.EnsureCanEnd(new DriverId(command.DriverId), outcomes) is { } refusal)
        {
            return TripRefused.Because<EndTripResult>(refusal);
        }

        var onTrip = await shipments.ListAsync(new ShipmentsToDispatchSpec([.. trip.ShipmentIds]), cancellationToken);
        var correlationId = new CorrelationId(command.CorrelationId);
        trip.End(correlationId, time.GetUtcNow(), outcomes, onTrip);

        var completed = trip.DomainEvents.OfType<TripCompletedEvent>().Single();

        // One save writes the status, the Stops, the Shipments and TripCompleted's outbox message together.
        await trips.SaveChangesAsync(cancellationToken);
        return new EndTripResult(
            trip.Number.Value,
            trip.Version,
            correlationId.Value,
            Numbers(completed.Delivered),
            Numbers(completed.NotDelivered),
            Numbers(completed.NotCollected));
    }

    private static string[] Numbers(IReadOnlyList<ShipmentNumber> numbers) => [.. numbers.Select(number => number.Value)];
}
