using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norvia.Simulator.Infrastructure.Data;

/// <summary>The Simulator database's EF Core model as the migrations see it, built without a database, for the generated database documentation (docs/applications).</summary>
public static class SimulatorDatabaseModel
{
    public static IModel Create()
    {
        var options = new DbContextOptionsBuilder<SimulatorDbContext>().UseSqlServer("Server=documentation").Options;
        using var context = new SimulatorDbContext(options);

        return context.GetService<IDesignTimeModel>().Model;
    }
}
