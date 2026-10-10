using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norvia.DriverApp.Infrastructure.Data;

[ExcludeFromCodeCoverage(Justification = "Used only by the EF Core tools to create migrations.")]
internal sealed class DriverAppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DriverAppDbContext>
{
    public DriverAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DriverAppDbContext>().UseSqlServer("Server=design-time;Database=driver-app").Options);
}
