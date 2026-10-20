namespace Norvia.Erp.Application.Reset;

/// <summary>The ERP's database as a reset sees it: transactional records go, master data stays (it comes from the seed file, which the ERP loads as it starts).</summary>
public interface ITransactionalData
{
    /// <summary>Deletes the Credit notes, Shipper invoices, Billable lines, Carrier costs, Carrier invoices (with their line decisions), the invoice number sequences and the outbox Messages.</summary>
    Task<IReadOnlyList<DeletedKind>> ClearAsync(CancellationToken cancellationToken);
}
