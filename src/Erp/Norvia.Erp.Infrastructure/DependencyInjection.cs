using Ardalis.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Infrastructure.Data;
using Norvia.Erp.Infrastructure.Outbox;
using Norvia.Erp.Infrastructure.Seed;

namespace Norvia.Erp.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "erp";
    public const string MessagingConnectionName = "messaging";

    public static IHostApplicationBuilder AddErpInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddErpDbContext();
        builder.Services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.Path), "The seed file doesn't exist.")
            .ValidateOnStart();
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped<IDomainEventDispatcher, MediatorDomainEventDispatcher>();
        builder.Services.AddScoped<IOutbox>(services => services.GetRequiredService<OutboxBuffer>());
        builder.Services.AddHostedService<LoadSeedOnStart>();

        return builder;
    }

    /// <summary>
    /// Sends committed outbox messages to Service Bus. The ERP's slices after this one raise the events (ADR 0003: events out); the
    /// process that runs it is the only one that does, so a message is not sent twice.
    /// </summary>
    public static IHostApplicationBuilder AddErpOutboxPublisher(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAzureServiceBusClient(MessagingConnectionName);
        builder.Services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddHostedService<OutboxPublisher>();

        return builder;
    }

    /// <summary>
    /// Registers what applying the ERP's migrations needs, and nothing else: migrations change the schema, never an
    /// aggregate, so no domain event is dispatched.
    /// </summary>
    public static IHostApplicationBuilder AddErpDatabaseMigrations(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddErpDbContext();
        builder.Services.AddSingleton<IDomainEventDispatcher, NoDomainEvents>();

        return builder;
    }

    /// <summary>
    /// Applies pending migrations. Only the ERP migrations run this, as a deployment step, never the ERP itself at
    /// startup (coding guidelines §5).
    /// </summary>
    public static async Task MigrateErpDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ErpDbContext>().Database.MigrateAsync(cancellationToken);
    }

    private static void AddErpDbContext(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<ErpDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString(DatabaseConnectionName)));
        builder.EnrichSqlServerDbContext<ErpDbContext>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<OutboxBuffer>();
    }
}
