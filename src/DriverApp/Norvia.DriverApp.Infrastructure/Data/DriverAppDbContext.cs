using Microsoft.EntityFrameworkCore;
using Norvia.DriverApp.Infrastructure.Outbox;

namespace Norvia.DriverApp.Infrastructure.Data;

/// <summary>The Driver app's own database: the latest version of each Trip it was sent, the Driver's reports, their outbox (ADR 0002).</summary>
internal sealed class DriverAppDbContext(DbContextOptions<DriverAppDbContext> options, OutboxBuffer outbox) : DbContext(options)
{
    // The events raised while handling a request are saved as outbox messages in the same transaction as the report that raised them.
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AddRange(outbox.TakePending());
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DriverAppDbContext).Assembly);
}
