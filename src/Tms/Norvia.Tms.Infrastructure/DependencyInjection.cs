using Ardalis.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.Data;
using Norvia.Tms.Infrastructure.Outbox;
using Norvia.Tms.Infrastructure.Shippers;
using Norvia.Tms.Infrastructure.TransportOrders;

namespace Norvia.Tms.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "tms";
    public const string MessagingConnectionName = "messaging";

    public static IHostApplicationBuilder AddTmsInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddDbContext<TmsDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString(DatabaseConnectionName)));
        builder.EnrichSqlServerDbContext<TmsDbContext>();
        builder.AddAzureServiceBusClient(MessagingConnectionName);

        builder.Services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);
        builder.Services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.Path), "The seed file doesn't exist.")
            .ValidateOnStart();

        builder.Services.AddOptions<TransportOrderRulesOptions>().BindConfiguration(TransportOrderRulesOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(services =>
            new TransportOrderRules(services.GetRequiredService<IOptions<TransportOrderRulesOptions>>().Value.MaxTotalWeightKg));
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped<IDomainEventDispatcher, MediatorDomainEventDispatcher>();
        builder.Services.AddScoped<OutboxBuffer>();
        builder.Services.AddScoped<IOutbox>(services => services.GetRequiredService<OutboxBuffer>());
        builder.Services.AddScoped<ITransportOrderNumbers, SqlTransportOrderNumbers>();
        builder.Services.AddSingleton<IShipperDirectory, SeedFileShipperDirectory>();
        builder.Services.AddHostedService<OutboxPublisher>();

        return builder;
    }

    /// <summary>Applies pending migrations when <see cref="DatabaseOptions.MigrateOnStartup"/> is on.</summary>
    public static async Task MigrateTmsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        if (!scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
        {
            return;
        }

        await scope.ServiceProvider.GetRequiredService<TmsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
