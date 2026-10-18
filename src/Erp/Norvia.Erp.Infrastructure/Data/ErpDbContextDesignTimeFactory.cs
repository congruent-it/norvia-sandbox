using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Norvia.Erp.Infrastructure.Outbox;

namespace Norvia.Erp.Infrastructure.Data;

[ExcludeFromCodeCoverage(Justification = "Used only by the EF Core tools to create migrations.")]
internal sealed class ErpDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ErpDbContext>
{
    public ErpDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer("Server=design-time;Database=erp").Options, new NoDomainEvents(),
            new OutboxBuffer(TimeProvider.System));
}
