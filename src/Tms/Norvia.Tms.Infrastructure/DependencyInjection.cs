using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.CarrierBookings;
using Norvia.Tms.Infrastructure.Carriers;
using Norvia.Tms.Infrastructure.Clock;
using Norvia.Tms.Infrastructure.Data;
using Norvia.Tms.Infrastructure.Outbox;
using Norvia.Tms.Infrastructure.ProofsOfDelivery;
using Norvia.Tms.Infrastructure.RateCards;
using Norvia.Tms.Infrastructure.Rating;
using Norvia.Tms.Infrastructure.Seed;
using Norvia.Tms.Infrastructure.Shipments;
using Norvia.Tms.Infrastructure.Shippers;
using Norvia.Tms.Infrastructure.TrackingEvents;
using Norvia.Tms.Infrastructure.TransportOrders;

namespace Norvia.Tms.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "tms";
    public const string MessagingConnectionName = "messaging";
    public const string BlobStorageConnectionName = "blobs";

    public static IHostApplicationBuilder AddTmsInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTmsDbContext();
        builder.Services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.Path), "The seed file doesn't exist.")
            .ValidateOnStart();

        builder.Services.AddOptions<TransportOrderRulesOptions>().BindConfiguration(TransportOrderRulesOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();

        builder.Services.AddSingleton(services =>
            new TransportOrderRules(services.GetRequiredService<IOptions<TransportOrderRulesOptions>>().Value.MaxTotalWeightKg));

        builder.Services.AddOptions<CarrierBookingOptions>().BindConfiguration(CarrierBookingOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(services => new CarrierBookingOverdueRules(services.GetRequiredService<IOptions<CarrierBookingOptions>>().Value.OverdueAfter));
        builder.Services.AddOptions<TrackingEventOptions>().BindConfiguration(TrackingEventOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(services => new TrackingEventRules(services.GetRequiredService<IOptions<TrackingEventOptions>>().Value.ClockSkew));
        builder.Services.AddOptions<ProofOfDeliveryOptions>().BindConfiguration(ProofOfDeliveryOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(services => new ProofOfDeliveryRules(services.GetRequiredService<IOptions<ProofOfDeliveryOptions>>().Value.ClockSkew));
        builder.Services.AddSingleton(services => new ProofOfDeliveryMissingRules(services.GetRequiredService<IOptions<ProofOfDeliveryOptions>>().Value.MissingAfter));
        builder.Services.AddOptions<ShipmentStaleOptions>().BindConfiguration(ShipmentStaleOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<ShipmentStaleOptions>>().Value;
            return new ShipmentStaleRules(options.CollectionGrace, options.SilenceLimit, options.DeliveryGrace);
        });
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped<IDomainEventDispatcher, MediatorDomainEventDispatcher>();
        builder.Services.AddScoped<IOutbox>(services => services.GetRequiredService<OutboxBuffer>());
        builder.Services.AddScoped<ITransportOrderNumbers, SqlTransportOrderNumbers>();
        builder.Services.AddScoped<IShipmentNumbers, SqlShipmentNumbers>();
        builder.Services.AddScoped<ICarrierBookingNumbers, SqlCarrierBookingNumbers>();
        builder.Services.AddScoped<IPipelineBehavior<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>, RetrySubmissionThatLostARace>();
        builder.Services.AddSingleton<IShipperDirectory, SeedFileShipperDirectory>();
        builder.Services.AddSingleton<IRateCardDirectory, SeedFileRateCardDirectory>();
        builder.Services.AddSingleton<ICarrierDirectory, SeedFileCarrierDirectory>();
        builder.Services.AddHostedService<LoadSeedOnStart>();

        return builder;
    }

    /// <summary>
    /// Rates accepted Transport Orders in the background (P2), triggered by each acceptance and on a timer. Only the
    /// process that takes in Transport Orders runs it, as it runs the outbox publisher.
    /// </summary>
    public static IHostApplicationBuilder AddTmsRatingWorker(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<RatingOptions>().BindConfiguration(RatingOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton<RatingSignal>();
        builder.Services.AddSingleton<IRatingTrigger>(services => services.GetRequiredService<RatingSignal>());
        builder.Services.AddHostedService<RatingWorker>();

        return builder;
    }

    /// <summary>
    /// Keeps Proof of delivery files in the TMS's own blob storage (P6). The TMS API stores the files and the TMS UI
    /// reads them in process (ADR 0028).
    /// </summary>
    public static IHostApplicationBuilder AddTmsProofOfDeliveryStorage(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAzureBlobServiceClient(BlobStorageConnectionName);
        builder.Services.AddSingleton<IProofOfDeliveryFiles, AzureBlobProofOfDeliveryFiles>();

        return builder;
    }

    /// <summary>
    /// Sends committed outbox messages to Service Bus. Only the process that takes in Transport Orders runs it: the
    /// TMS UI shares the database, and a second publisher would send the same message twice.
    /// </summary>
    public static IHostApplicationBuilder AddTmsOutboxPublisher(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAzureServiceBusClient(MessagingConnectionName);
        builder.Services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddHostedService<OutboxPublisher>();

        return builder;
    }

    /// <summary>
    /// Registers what applying the TMS's migrations needs, and nothing else: migrations change the schema, never an
    /// aggregate, so no domain event is dispatched.
    /// </summary>
    public static IHostApplicationBuilder AddTmsDatabaseMigrations(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTmsDbContext();
        builder.Services.AddSingleton<IDomainEventDispatcher, NoDomainEvents>();

        return builder;
    }

    /// <summary>
    /// Applies pending migrations. Only the TMS migrations run this, as a deployment step, never the TMS itself at
    /// startup (coding guidelines §5).
    /// </summary>
    public static async Task MigrateTmsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TmsDbContext>().Database.MigrateAsync(cancellationToken);
    }

    /// <summary>The <see cref="TmsDbContext"/> and what it is built with, apart from its domain event dispatcher.</summary>
    private static void AddTmsDbContext(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<TmsDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString(DatabaseConnectionName)));
        builder.EnrichSqlServerDbContext<TmsDbContext>();
        builder.Services.AddSingleton(ClockFor(builder.Environment, builder.Configuration));
        builder.Services.AddScoped<OutboxBuffer>();
    }

    /// <summary>
    /// The system's clock, or, on a development host told where, a clock a test host sets by writing a file
    /// (<c>Clock:ControlFile</c>): never in a deployed environment, where nothing may set the TMS's time.
    /// </summary>
    internal static TimeProvider ClockFor(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration["Clock:ControlFile"] is { Length: > 0 } path
            ? new FileControlledTimeProvider(path)
            : TimeProvider.System;
}
