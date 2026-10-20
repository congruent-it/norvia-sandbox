using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norvia.Tms.Infrastructure.Data;

/// <summary>The TMS database's EF Core model as the migrations see it, built without a database, for the generated database documentation (docs/applications).</summary>
public static class TmsDatabaseModel
{
    public static IModel Create()
    {
        var options = new DbContextOptionsBuilder<TmsDbContext>().UseSqlServer("Server=documentation").Options;
        using var context = new TmsDbContext(options, new NoDomainEvents(), new Outbox.OutboxBuffer(TimeProvider.System));

        return context.GetService<IDesignTimeModel>().Model;
    }
}
