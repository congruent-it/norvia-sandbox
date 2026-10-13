using Microsoft.EntityFrameworkCore;

namespace Norvia.Simulator.Infrastructure.Data;

/// <summary>The Simulator's own database: its runs, their plans and every step's outcome (ADR 0002).</summary>
internal sealed class SimulatorDbContext(DbContextOptions<SimulatorDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SimulatorDbContext).Assembly);
}
