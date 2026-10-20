using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norvia.Erp.Infrastructure.Data;

/// <summary>The ERP database's EF Core model as the migrations see it, built without a database, for the generated database documentation (docs/applications).</summary>
public static class ErpDatabaseModel
{
    public static IModel Create()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer("Server=documentation").Options;
        using var context = new ErpDbContext(options, new NoDomainEvents(), new Outbox.OutboxBuffer(TimeProvider.System));

        return context.GetService<IDesignTimeModel>().Model;
    }
}
