using Microsoft.EntityFrameworkCore;
using Norvia.PartnerPortal.Application.Reset;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.ShipperInvoices;
using Norvia.PartnerPortal.Domain.ShipperNotifications;
using Norvia.PartnerPortal.Infrastructure.Data;

namespace Norvia.PartnerPortal.Infrastructure.Reset;

/// <summary>
/// Deletes the Partner portal's records in one transaction: the Shippers' Bookings, notifications and invoices, the Carriers' booking requests with the shipments, tracking
/// and Proofs of delivery they hold, and the Personas' modes, which the seed file sets again. The Personas themselves come from the seed file and are not in the database.
/// </summary>
internal sealed class SqlTransactionalData(PartnerPortalDbContext dbContext) : ITransactionalData
{
    public async Task<IReadOnlyList<DeletedKind>> ClearAsync(CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var deleted = new List<DeletedKind>
            {
                await DeleteAsync<Booking>("Bookings", cancellationToken),
                await DeleteAsync<ShipperNotification>("Shipper notifications", cancellationToken),
                await DeleteAsync<ShipperInvoice>("Shipper invoices", cancellationToken),
                await DeleteAsync<CarrierBookingRequest>("Carrier booking requests", cancellationToken),
                await DeleteAsync<CarrierAnswerSettings>("Answer modes", cancellationToken),
                await DeleteAsync<CarrierTrackingSettings>("Tracking modes", cancellationToken),
            };

            await transaction.CommitAsync(cancellationToken);
            return (IReadOnlyList<DeletedKind>)deleted;
        });
    }

    private async Task<DeletedKind> DeleteAsync<T>(string kind, CancellationToken cancellationToken)
        where T : class =>
        new(kind, await dbContext.Set<T>().ExecuteDeleteAsync(cancellationToken));
}
