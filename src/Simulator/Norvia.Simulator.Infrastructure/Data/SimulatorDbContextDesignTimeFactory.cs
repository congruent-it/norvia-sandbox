using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norvia.Simulator.Infrastructure.Data;

[ExcludeFromCodeCoverage(Justification = "Used only by the EF Core tools to create migrations.")]
internal sealed class SimulatorDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SimulatorDbContext>
{
    public SimulatorDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SimulatorDbContext>().UseSqlServer("Server=design-time;Database=simulator").Options);
}
