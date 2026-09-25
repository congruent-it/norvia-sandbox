using Microsoft.EntityFrameworkCore;

namespace Norvia.PartnerPortal.Infrastructure.Data;

/// <summary>The Partner portal's own database: the outside parties' records of what they sent Norvia (ADR 0002).</summary>
internal sealed class PartnerPortalDbContext(DbContextOptions<PartnerPortalDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PartnerPortalDbContext).Assembly);
}
