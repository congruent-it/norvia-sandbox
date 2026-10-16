using Microsoft.EntityFrameworkCore;
using Norvia.Tms.Application.Reset;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Infrastructure.Data;
using Norvia.Tms.Infrastructure.Outbox;

namespace Norvia.Tms.Infrastructure.Reset;

/// <summary>
/// Deletes the TMS's transactional records in one transaction, children before the rows they point at. The Rate Cards, Carriers, Vehicles, Drivers
/// and Shippers are not in the database (they come from the seed file) and the Planning days are the calendar, so they stay; the Planner's changes to the
/// Rosters, Absences and Off the road days go, which puts the seed file's back in force.
/// </summary>
internal sealed class SqlTransactionalData(TmsDbContext dbContext) : ITransactionalData
{
    private static readonly string[] Sequences =
    [
        TmsDbContext.TransportOrderNumberSequence,
        TmsDbContext.ShipmentNumberSequence,
        TmsDbContext.CarrierBookingNumberSequence,
        TmsDbContext.ExceptionNumberSequence,
        TmsDbContext.TripNumberSequence,
    ];

    public async Task<IReadOnlyList<DeletedKind>> ClearAsync(CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var deleted = new List<DeletedKind>
            {
                await DeleteAsync<ShipperNotification>("Shipper notifications", cancellationToken),
                await DeleteAsync<TrackingEvent>("Tracking events", cancellationToken),
                await DeleteAsync<ProofOfDelivery>("Proof of delivery records", cancellationToken),
                await DeleteAsync<CarrierBooking>("Carrier bookings", cancellationToken),
                await DeleteAsync<ExceptionCase>("Exceptions", cancellationToken),
                await DeleteAsync<Trip>("Trips", cancellationToken),
                await DeleteAsync<Shipment>("Shipments", cancellationToken),
                await DeleteAsync<TransportOrder>("Transport Orders", cancellationToken),
                await DeleteAsync<OutboxMessage>("Messages", cancellationToken),
                await DeleteAsync<Absence>("Absences", cancellationToken),
                await DeleteAsync<OffTheRoadDay>("Off the road days", cancellationToken),
                await DeleteAsync<Roster>("Roster changes", cancellationToken),
            };

            foreach (var sequence in Sequences)
            {
                // The names are this class's own constants, never data.
#pragma warning disable EF1002
                await dbContext.Database.ExecuteSqlRawAsync($"ALTER SEQUENCE [{sequence}] RESTART WITH 1", cancellationToken);
#pragma warning restore EF1002
            }

            await transaction.CommitAsync(cancellationToken);
            return (IReadOnlyList<DeletedKind>)deleted;
        });
    }

    private async Task<DeletedKind> DeleteAsync<T>(string kind, CancellationToken cancellationToken)
        where T : class =>
        new(kind, await dbContext.Set<T>().ExecuteDeleteAsync(cancellationToken));
}
