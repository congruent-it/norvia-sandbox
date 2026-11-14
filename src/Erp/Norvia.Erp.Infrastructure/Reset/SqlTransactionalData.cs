using Microsoft.EntityFrameworkCore;
using Norvia.Erp.Application.Reset;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Infrastructure.Data;
using Norvia.Erp.Infrastructure.Outbox;

namespace Norvia.Erp.Infrastructure.Reset;

/// <summary>
/// Deletes the ERP's transactional records in one transaction: Credit notes before the Shipper invoices they credit, the invoice and Credit note number
/// sequences (so numbers begin again), and the outbox. The Shippers, Carriers, Rate Cards, Vehicles and Drivers are master data from the seed file and stay.
/// </summary>
internal sealed class SqlTransactionalData(ErpDbContext dbContext) : ITransactionalData
{
    public async Task<IReadOnlyList<DeletedKind>> ClearAsync(CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var deleted = new List<DeletedKind>
            {
                await DeleteAsync<CreditNote>("Credit notes", cancellationToken),
                await DeleteAsync<ShipperInvoice>("Shipper invoices", cancellationToken),
                await DeleteAsync<BillableLine>("Billable lines", cancellationToken),
                await DeleteAsync<CarrierCost>("Carrier costs", cancellationToken),
                await DeleteAsync<CarrierInvoice>("Carrier invoices", cancellationToken),
                await DeleteAsync<InvoiceNumberSequence>("Invoice number sequences", cancellationToken),
                await DeleteAsync<CreditNoteNumberSequence>("Credit note number sequences", cancellationToken),
                await DeleteAsync<OutboxMessage>("Messages", cancellationToken),
            };

            await transaction.CommitAsync(cancellationToken);
            return (IReadOnlyList<DeletedKind>)deleted;
        });
    }

    private async Task<DeletedKind> DeleteAsync<T>(string kind, CancellationToken cancellationToken)
        where T : class =>
        new(kind, await dbContext.Set<T>().ExecuteDeleteAsync(cancellationToken));
}
