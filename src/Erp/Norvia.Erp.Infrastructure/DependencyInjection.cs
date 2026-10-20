using Ardalis.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.Reset;
using Norvia.Erp.Infrastructure.CarrierInvoices;
using Norvia.Erp.Infrastructure.Data;
using Norvia.Erp.Infrastructure.Outbox;
using Norvia.Erp.Infrastructure.Reset;
using Norvia.Erp.Infrastructure.Seed;

namespace Norvia.Erp.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "erp";
    public const string MessagingConnectionName = "messaging";
    public const string EdgeMessagingConnectionName = "messaging-edge";

    public static IHostApplicationBuilder AddErpInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddErpDbContext();
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped<IDomainEventDispatcher, MediatorDomainEventDispatcher>();
        builder.Services.AddScoped<IConflictRetrier, EfConflictRetrier>();
        builder.Services.AddScoped<IOutbox>(services => services.GetRequiredService<OutboxBuffer>());
        builder.Services.AddOptions<MatchingOptions>().BindConfiguration(MatchingOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<IMatchingTolerance>(services => services.GetRequiredService<IOptions<MatchingOptions>>().Value);

        return builder;
    }

    /// <summary>
    /// Loads the master data from the seed file as the application starts. One process does it: the ERP UI, which the ERP API waits for.
    /// </summary>
    public static IHostApplicationBuilder AddErpSeedLoading(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.Path), "The seed file doesn't exist.")
            .ValidateOnStart();
        builder.Services.AddHostedService<LoadSeedOnStart>();

        return builder;
    }

    /// <summary>
    /// Sends committed outbox messages to Service Bus. The ERP raises its events through the outbox (ADR 0003: events out); the
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
    /// The reset the Simulator's Reset estate calls (ADR 0039): guarded by the <c>Reset</c> setting, which is off by default, and by the outbox and the
    /// topic subscriptions it names being empty. Only the API has the Service Bus clients to peek them with: <c>messaging</c>, and
    /// <c>messaging-edge</c> where the estate keeps the Edge topics in a namespace of their own (locally).
    /// </summary>
    public static IHostApplicationBuilder AddErpReset(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddKeyedAzureServiceBusClient(MessagingConnectionName);
        if (builder.Configuration.GetConnectionString(EdgeMessagingConnectionName) is not null)
        {
            builder.AddKeyedAzureServiceBusClient(EdgeMessagingConnectionName);
        }

        builder.Services.AddOptions<ResetOptions>().BindConfiguration(ResetOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<IResetSwitch, ConfiguredResetSwitch>();
        builder.Services.AddScoped<IQuietCheck, ServiceBusQuietCheck>();
        builder.Services.AddScoped<ITransactionalData, SqlTransactionalData>();

        return builder;
    }

    /// <summary>
    /// The reset's services for a process that finds the reset handlers (the mediator scans the whole application assembly) but does not offer the reset, such as the
    /// ERP UI: the switch stays off, and nothing in the process can peek a subscription.
    /// </summary>
    public static IHostApplicationBuilder AddErpResetUnavailable(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<ResetOptions>().BindConfiguration(ResetOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<IResetSwitch, ConfiguredResetSwitch>();
        builder.Services.AddScoped<IQuietCheck, UnavailableQuietCheck>();
        builder.Services.AddScoped<ITransactionalData, SqlTransactionalData>();

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
