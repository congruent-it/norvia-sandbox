using Ardalis.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Norvia.Erp.Infrastructure.Outbox;

namespace Norvia.Erp.Infrastructure.Data;

internal sealed class ErpDbContext(DbContextOptions<ErpDbContext> options, IDomainEventDispatcher dispatcher, OutboxBuffer outbox)
    : DbContext(options)
{
    // Domain events are handled before the commit, and the integration events their handlers raise are saved as
    // outbox messages in the same transaction as the change that raised them. Everything else works with data
    // through the repositories.
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var withEvents = ChangeTracker.Entries<HasDomainEventsBase>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToArray();
        await dispatcher.DispatchAndClearEvents(withEvents);
        AddRange(outbox.TakePending());

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpDbContext).Assembly);
}
