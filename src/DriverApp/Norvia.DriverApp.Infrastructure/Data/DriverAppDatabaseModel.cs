using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norvia.DriverApp.Infrastructure.Data;

/// <summary>The Driver app database's EF Core model as the migrations see it, built without a database, for the generated database documentation (docs/applications).</summary>
public static class DriverAppDatabaseModel
{
    public static IModel Create()
    {
        var options = new DbContextOptionsBuilder<DriverAppDbContext>().UseSqlServer("Server=documentation").Options;
        using var context = new DriverAppDbContext(options, new Outbox.OutboxBuffer(TimeProvider.System));

        return context.GetService<IDesignTimeModel>().Model;
    }
}
