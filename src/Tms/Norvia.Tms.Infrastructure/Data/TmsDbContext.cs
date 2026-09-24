using Ardalis.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.Outbox;

namespace Norvia.Tms.Infrastructure.Data;

public sealed class TmsDbContext(DbContextOptions<TmsDbContext> options, IDomainEventDispatcher dispatcher) : DbContext(options)
{
    public const string TransportOrderNumberSequence = "TransportOrderNumbers";

    public DbSet<TransportOrder> TransportOrders => Set<TransportOrder>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // Domain events are handled before the commit, so whatever their handlers add (outbox messages above all) is
    // saved in the same transaction as the change that raised them.
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var withEvents = ChangeTracker.Entries<HasDomainEventsBase>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToArray();
        await dispatcher.DispatchAndClearEvents(withEvents);

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(TransportOrderNumberSequence).StartsAt(1).IncrementsBy(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TmsDbContext).Assembly);
    }
}
