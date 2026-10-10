using Microsoft.EntityFrameworkCore;

namespace Norvia.DriverApp.Infrastructure.Data;

/// <summary>The Driver app's own database: the latest version of each Trip it was sent, and its demo settings (ADR 0002).</summary>
internal sealed class DriverAppDbContext(DbContextOptions<DriverAppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DriverAppDbContext).Assembly);
}
