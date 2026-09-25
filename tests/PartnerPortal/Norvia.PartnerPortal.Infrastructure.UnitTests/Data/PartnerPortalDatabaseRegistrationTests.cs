using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.PartnerPortal.Infrastructure.Data;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Data;

/// <summary>How the portal connects to its database, which in Azure is serverless and pauses when nobody uses it.</summary>
public sealed class PartnerPortalDatabaseRegistrationTests
{
    [Fact]
    public void ThePortal_WaitsLongEnoughToConnect_ForAPausedDatabaseToResume()
    {
        // As Aspire gives it for Azure SQL: no Connect Timeout, so SqlClient's 15 seconds, and resuming takes up to a minute.
        var connectTimeout = ConnectTimeoutFor("Server=tcp:sql-norvia.database.windows.net,1433;Encrypt=True;Database=partner-portal");

        connectTimeout.Should().BeGreaterThanOrEqualTo(90);
    }

    [Fact]
    public void ALongerConnectTimeoutInTheConnectionString_IsKept()
    {
        ConnectTimeoutFor("Server=localhost;Database=partner-portal;Connect Timeout=120").Should().Be(120);
    }

    private static int ConnectTimeoutFor(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:partner-portal"] = connectionString });
        builder.AddPartnerPortalDatabaseMigrations();
        using var services = builder.Build();
        using var scope = services.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<PartnerPortalDbContext>();
        return new SqlConnectionStringBuilder(context.Database.GetConnectionString()).ConnectTimeout;
    }
}
