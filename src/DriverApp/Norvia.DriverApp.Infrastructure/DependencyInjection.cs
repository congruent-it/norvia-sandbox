using Ardalis.SharedKernel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.DriverApp.Application;
using Norvia.DriverApp.Application.Drivers;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Application.Faults;
using Norvia.DriverApp.Application.Reports.Deliver;
using Norvia.DriverApp.Application.Reset;
using Norvia.DriverApp.Infrastructure.Data;
using Norvia.DriverApp.Infrastructure.Drivers;
using Norvia.DriverApp.Infrastructure.Faults;
using Norvia.DriverApp.Infrastructure.Outbox;
using Norvia.DriverApp.Infrastructure.ProofsOfDelivery;
using Norvia.DriverApp.Infrastructure.Reset;

namespace Norvia.DriverApp.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "driver-app";
    public const string MessagingConnectionName = "messaging";
    public const string BlobStorageConnectionName = "driver-app-blobs";

    public static IHostApplicationBuilder AddDriverAppInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddDriverAppDbContext();
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped<IConflictRetrier, EfConflictRetrier>();

        // The faults the presenter can switch on: off by default, and each expires by itself. Only the API exposes the switches.
        builder.AddFaults(DriverAppFaultDefinitions.All);
        builder.Services.AddSingleton<IDriverAppFaults, DriverAppFaults>();

        return builder;
    }

    /// <summary>
    /// Sends committed outbox messages to Service Bus. Only the API runs it: the Driver's screens share the database, and a second publisher would
    /// send the same message twice.
    /// </summary>
    public static IHostApplicationBuilder AddDriverAppOutboxPublisher(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAzureServiceBusClient(MessagingConnectionName);
        builder.Services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddHostedService<OutboxPublisher>();

        return builder;
    }

    /// <summary>
    /// The reset the Simulator's Reset estate calls (ADR 0039): guarded by the <c>Reset</c> setting, which is off by default, and by the topic
    /// subscriptions it names being empty. Only the API has the Service Bus client to peek them with.
    /// </summary>
    public static IHostApplicationBuilder AddDriverAppReset(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<ResetOptions>().BindConfiguration(ResetOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<IResetSwitch, ConfiguredResetSwitch>();
        builder.Services.AddScoped<IQuietCheck, ServiceBusQuietCheck>();
        builder.Services.AddScoped<ITransactionalData, SqlTransactionalData>();

        return builder;
    }

    /// <summary>
    /// The reset's services for a process that finds the reset handlers (the mediator scans the whole application assembly) but does not offer the reset, such as the Driver
    /// app's screens: the switch stays off, and nothing in the process can peek a subscription.
    /// </summary>
    public static IHostApplicationBuilder AddDriverAppResetUnavailable(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<ResetOptions>().BindConfiguration(ResetOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<IResetSwitch, ConfiguredResetSwitch>();
        builder.Services.AddScoped<IQuietCheck, UnavailableQuietCheck>();
        builder.Services.AddScoped<ITransactionalData, SqlTransactionalData>();

        return builder;
    }

    /// <summary>The transit container for the files of a Proof of delivery (P12). Only the screens, where the Driver gives the files, need it.</summary>
    public static IHostApplicationBuilder AddDriverAppProofOfDeliveryTransit(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAzureBlobServiceClient(BlobStorageConnectionName);
        builder.Services.AddSingleton<IProofOfDeliveryTransit, AzureBlobProofOfDeliveryTransit>();
        return builder;
    }

    /// <summary>The Drivers a person picks to sign in as, read from the seed file. Only the screens need them.</summary>
    public static IHostApplicationBuilder AddDriverAppSeedDrivers(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.Path), "The seed file doesn't exist.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IDriverDirectory, SeedFileDriverDirectory>();
        return builder;
    }

    /// <summary>Registers what applying the Driver app's migrations needs, and nothing else.</summary>
    public static IHostApplicationBuilder AddDriverAppDatabaseMigrations(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddDriverAppDbContext();
        return builder;
    }

    /// <summary>
    /// Applies pending migrations. Only the Driver app's migrations run this, as a deployment step, never the Driver app
    /// itself at startup (coding guidelines §5).
    /// </summary>
    public static async Task MigrateDriverAppDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DriverAppDbContext>().Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// In Azure the database is serverless and pauses after an hour unused, and the first connection then waits while it
    /// resumes, which can take a minute. SqlClient gives up after 15 seconds by default, which would lose the Trip version.
    /// </summary>
    private static string? WithTimeoutForAResume(string? connectionString)
    {
        const int resumeSeconds = 90;
        if (connectionString is null)
        {
            return null;
        }

        var connection = new SqlConnectionStringBuilder(connectionString);
        connection.ConnectTimeout = Math.Max(connection.ConnectTimeout, resumeSeconds);
        return connection.ConnectionString;
    }

    private static void AddDriverAppDbContext(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<DriverAppDbContext>(options =>
            options.UseSqlServer(WithTimeoutForAResume(builder.Configuration.GetConnectionString(DatabaseConnectionName))));
        builder.EnrichSqlServerDbContext<DriverAppDbContext>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<OutboxBuffer>();
        builder.Services.AddScoped<IDriverEventOutbox>(services => services.GetRequiredService<OutboxBuffer>());
    }
}
