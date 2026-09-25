using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norvia.PartnerPortal.Infrastructure.Data;

[ExcludeFromCodeCoverage(Justification = "Used only by the EF Core tools to create migrations.")]
internal sealed class PartnerPortalDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PartnerPortalDbContext>
{
    public PartnerPortalDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PartnerPortalDbContext>().UseSqlServer("Server=design-time;Database=partner-portal").Options);
}
