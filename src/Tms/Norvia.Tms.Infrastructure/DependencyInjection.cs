using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.Tms.Application;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Billing.Release;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Availability;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Application.Reset;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.AddShipments;
using Norvia.Tms.Application.Trips.Change;
using Norvia.Tms.Application.Trips.Create;
using Norvia.Tms.Application.Trips.ReorderStops;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.CarrierBookings;
using Norvia.Tms.Infrastructure.Carriers;
using Norvia.Tms.Infrastructure.Clock;
using Norvia.Tms.Infrastructure.Data;
using Norvia.Tms.Infrastructure.Exceptions;
using Norvia.Tms.Infrastructure.Fleet;
using Norvia.Tms.Infrastructure.Outbox;
using Norvia.Tms.Infrastructure.ProofsOfDelivery;
using Norvia.Tms.Infrastructure.RateCards;
using Norvia.Tms.Infrastructure.Rating;
using Norvia.Tms.Infrastructure.Reset;
using Norvia.Tms.Infrastructure.Seed;
using Norvia.Tms.Infrastructure.Shipments;
using Norvia.Tms.Infrastructure.Shippers;
using Norvia.Tms.Infrastructure.TrackingEvents;
using Norvia.Tms.Infrastructure.TransportOrders;
using Norvia.Tms.Infrastructure.Trips;

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
        builder.Services.AddScoped<ExceptionCauses>();
        builder.Services.AddScoped<BillingCheck>();
        builder.Services.AddScoped<ReleaseEligibilityCheck>();
        builder.Services.AddSingleton(services => new TrackingEventRules(services.GetRequiredService<IOptions<TrackingEventOptions>>().Value.ClockSkew));
        builder.Services.AddOptions<ProofOfDeliveryOptions>().BindConfiguration(ProofOfDeliveryOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(services => new ProofOfDeliveryRules(services.GetRequiredService<IOptions<ProofOfDeliveryOptions>>().Value.ClockSkew));
        builder.Services.AddSingleton(services => MissingRulesFor(services.GetRequiredService<IOptions<ProofOfDeliveryOptions>>().Value));
        builder.Services.AddOptions<ShipmentStaleOptions>().BindConfiguration(ShipmentStaleOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(services => StaleRulesFor(services.GetRequiredService<IOptions<ShipmentStaleOptions>>().Value));
        builder.Services.AddOptions<LateDeliveryOptions>().BindConfiguration(LateDeliveryOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(services => LateDeliveryRulesFor(services.GetRequiredService<IOptions<LateDeliveryOptions>>().Value));
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped<IConflictRetrier, EfConflictRetrier>();
        builder.Services.AddScoped<IDomainEventDispatcher, MediatorDomainEventDispatcher>();
        builder.Services.AddScoped<IOutbox>(services => services.GetRequiredService<OutboxBuffer>());
        builder.Services.AddScoped<ITransportOrderNumbers, SqlTransportOrderNumbers>();
        builder.Services.AddScoped<IShipmentNumbers, SqlShipmentNumbers>();
        builder.Services.AddScoped<ICarrierBookingNumbers, SqlCarrierBookingNumbers>();
        builder.Services.AddScoped<IExceptionNumbers, SqlExceptionNumbers>();
        builder.Services.AddScoped<ITripNumbers, SqlTripNumbers>();
        builder.Services.AddScoped<TripPlanningChecks>();
        builder.Services.AddScoped<TripNeedsCheck>();
        builder.Services.AddScoped<TripShipments>();
        builder.Services.AddScoped<TripOutcomes>();
        builder.Services.AddScoped<TripAmendments>();
        builder.Services.AddScoped<IPipelineBehavior<AddShipmentsToTripCommand, Result<AddShipmentsToTripResult>>, RetryTripThatLostARace<AddShipmentsToTripCommand, Result<AddShipmentsToTripResult>>>();
        builder.Services.AddScoped<IPipelineBehavior<CreateDraftTripCommand, Result<CreateDraftTripResult>>, RetryTripThatLostARace<CreateDraftTripCommand, Result<CreateDraftTripResult>>>();
        builder.Services.AddScoped<IPipelineBehavior<ChangeTripCommand, Result>, RetryTripThatLostARace<ChangeTripCommand, Result>>();
        builder.Services.AddScoped<IPipelineBehavior<ReorderTripStopsCommand, Result>, RetryTripThatLostARace<ReorderTripStopsCommand, Result>>();
        builder.Services.AddScoped<IPipelineBehavior<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>, RetrySubmissionThatLostARace>();
        builder.Services.AddSingleton<SeedFileShipperDirectory>();
        builder.Services.AddSingleton<ReplicatedShipperDirectory>();
        builder.Services.AddSingleton<IShipperDirectory>(services => services.GetRequiredService<ReplicatedShipperDirectory>());
        builder.Services.AddSingleton<IShipperReplicas>(services => services.GetRequiredService<ReplicatedShipperDirectory>());
        builder.Services.AddSingleton<IRateCardDirectory, SeedFileRateCardDirectory>();
        builder.Services.AddSingleton<ICarrierRateCardDirectory, SeedFileCarrierRateCardDirectory>();
        builder.Services.AddSingleton<SeedFileCarrierDirectory>();
        builder.Services.AddSingleton<ReplicatedCarrierDirectory>();
        builder.Services.AddSingleton<ICarrierDirectory>(services => services.GetRequiredService<ReplicatedCarrierDirectory>());
        builder.Services.AddSingleton<ICarrierReplicas>(services => services.GetRequiredService<ReplicatedCarrierDirectory>());
        builder.Services.AddSingleton<IVehicleDirectory, SeedFileVehicleDirectory>();
        builder.Services.AddSingleton<IDriverDirectory, SeedFileDriverDirectory>();
        builder.Services.AddSingleton<IRosterDirectory, SeedFileRosterDirectory>();
        builder.Services.AddScoped<EffectiveRosters>();
        builder.Services.AddScoped<DayLoads>();
        builder.Services.AddScoped<LoadCheckWarnings>();
        builder.Services.AddScoped<TripAvailabilityRules>();
        builder.Services.AddOptions<PlanningHorizonOptions>().BindConfiguration(PlanningHorizonOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out _), "PlanningHorizon:TimeZone isn't a time zone this host knows.")
            .ValidateOnStart();
        builder.Services.AddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<PlanningHorizonOptions>>().Value;
            return new PlanningHorizonRules(options.Days, TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone));
        });
        builder.Services.AddOptions<DrivingHourOptions>().BindConfiguration(DrivingHourOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.IsConsistent, "DrivingHours: the extended daily limit can't be below the daily one, nor the fortnightly below the weekly.")
            .ValidateOnStart();
        builder.Services.AddSingleton(services => services.GetRequiredService<IOptions<DrivingHourOptions>>().Value.ToLimits());
        builder.Services.AddScoped<IPlannedDrivingHours, TripPlannedDrivingHours>();
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
    /// Opens the days of the Planning horizon every midnight (P10), and seeds the demo's Over capacity day the first time. Only the process
    /// that takes in Transport Orders runs them, as it runs the rating worker; the TMS UI shares the database and only reads the days.
    /// </summary>
    public static IHostApplicationBuilder AddTmsPlanningHorizonJob(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHostedService<PlanningHorizonWorker>();
        builder.Services.AddSingleton(services => DemoSeed.Read(services.GetRequiredService<IOptions<SeedOptions>>()));
        builder.Services.AddHostedService<DemoOverCapacityDaySeeder>();
        builder.Services.AddHostedService<DemoTripDaySeeder>();
        builder.Services.AddHostedService<DemoTripExecutionSeeder>();

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
    /// The reset the Simulator's Reset estate calls (ADR 0039): guarded by the <c>Reset</c> setting, which is off by default, and by the topic
    /// subscriptions it names being empty. Only the process that publishes to Service Bus has the client to peek them with.
    /// </summary>
    public static IHostApplicationBuilder AddTmsReset(this IHostApplicationBuilder builder)
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
    /// The reset's services for a process that finds the reset handlers (the mediator scans the whole application assembly) but does not offer the reset, such as the TMS UI:
    /// the switch stays off, and nothing in the process can peek a subscription.
    /// </summary>
    public static IHostApplicationBuilder AddTmsResetUnavailable(this IHostApplicationBuilder builder)
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

    /// <summary>P5's Stale rules, with no threshold when the settings leave them unset (the as-found estate).</summary>
    internal static ShipmentStaleRules StaleRulesFor(ShipmentStaleOptions options) =>
        options.ThresholdsSet ? new ShipmentStaleRules(options.CollectionGrace, options.SilenceLimit, options.DeliveryGrace) : ShipmentStaleRules.Unset;

    /// <summary>P6's missing rule, with no threshold when the settings leave it unset (the as-found estate).</summary>
    internal static ProofOfDeliveryMissingRules MissingRulesFor(ProofOfDeliveryOptions options) =>
        options.MissingThresholdSet ? new ProofOfDeliveryMissingRules(options.MissingAfter) : ProofOfDeliveryMissingRules.Unset;

    /// <summary>P7's Late delivery rules, with the time-based check off when the settings switch it off (the as-found estate).</summary>
    internal static LateDeliveryRules LateDeliveryRulesFor(LateDeliveryOptions options) =>
        options.CheckEnabled ? new LateDeliveryRules(options.Grace) : LateDeliveryRules.SwitchedOff;
}
