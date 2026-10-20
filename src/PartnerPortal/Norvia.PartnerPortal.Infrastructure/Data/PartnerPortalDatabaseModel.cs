using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norvia.PartnerPortal.Infrastructure.Data;

/// <summary>The Partner portal database's EF Core model as the migrations see it, built without a database, for the generated database documentation (docs/applications).</summary>
public static class PartnerPortalDatabaseModel
{
    public static IModel Create()
    {
        var options = new DbContextOptionsBuilder<PartnerPortalDbContext>().UseSqlServer("Server=documentation").Options;
        using var context = new PartnerPortalDbContext(options);

        return context.GetService<IDesignTimeModel>().Model;
    }
}
