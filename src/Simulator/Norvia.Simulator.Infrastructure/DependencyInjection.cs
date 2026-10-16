using System.ComponentModel.DataAnnotations;
using Ardalis.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Runs.Execute;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Infrastructure.Data;
using Norvia.Simulator.Infrastructure.DriverApi;
using Norvia.Simulator.Infrastructure.Entra;
using Norvia.Simulator.Infrastructure.Execution;
using Norvia.Simulator.Infrastructure.PartnerApi;
using Norvia.Simulator.Infrastructure.TmsApi;
using Norvia.Simulator.Infrastructure.World;
using Polly;

namespace Norvia.Simulator.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "simulator";

    public static IHostApplicationBuilder AddSimulatorInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSimulatorDbContext();
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));

        builder.Services.AddOptions<SimulatorOptions>().BindConfiguration(SimulatorOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<SimulatorOptions>>().Value;
            return new SimulationLimits(options.MaxOrdersPerRun, options.MaxConcurrentRequests);
        });

        builder.Services.AddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<SimulatorOptions>>().Value;
            return new CarrierPacing(TimeSpan.FromSeconds(options.ExceptionPollSeconds), options.ExceptionPollAttempts);
        });

        builder.Services.AddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<SimulatorOptions>>().Value;
            return new DriverPacing(TimeSpan.FromSeconds(options.DriverPollSeconds), options.DriverPollAttempts);
        });

        builder.Services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.Path), "The seed file doesn't exist.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IWorldSource, SeedFileWorldSource>();
        builder.Services.AddSingleton<ISeedSource, RandomSeedSource>();

        builder.Services.AddSingleton(_ => new SimulatorActivities(builder.Environment.ApplicationName));
        builder.AddNorviaPartnerApi();
        builder.AddNorviaTmsApi();
        builder.AddNorviaDriverApp();

        builder.Services.AddSingleton<RunExecutor>();
        builder.Services.AddSingleton<IRunExecution>(services => services.GetRequiredService<RunExecutor>());
        builder.Services.AddHostedService(services => services.GetRequiredService<RunExecutor>());

        return builder;
    }

    /// <summary>Registers what applying the Simulator's migrations needs, and nothing else.</summary>
    public static IHostApplicationBuilder AddSimulatorDatabaseMigrations(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddSimulatorDbContext();
        return builder;
    }

    /// <summary>Applies pending migrations. Only the Simulator's migrations run this, as a deployment step, never the Simulator itself at startup (coding guidelines §5).</summary>
    public static async Task MigrateSimulatorDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SimulatorDbContext>().Database.MigrateAsync(cancellationToken);
    }

    private static bool IsValid(object options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), validationResults: null, validateAllProperties: true);

    private static void AddSimulatorDbContext(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<SimulatorDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString(DatabaseConnectionName)));
        builder.EnrichSqlServerDbContext<SimulatorDbContext>();
        builder.Services.AddSingleton(TimeProvider.System);
    }

    /// <summary>The Norvia Partner API client, as the Personas call it. Locally the Simulator signs the Persona tokens itself (ADR 0023); in Azure they come from Entra (ADR 0042).</summary>
    private static void AddNorviaPartnerApi(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<PartnerApiOptions>().BindConfiguration(PartnerApiOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => (options.Entra is null) != (options.LocalTokens is null), "Configure either PartnerApi:Entra or PartnerApi:LocalTokens.")
            // Data annotations don't look inside the nested options, so whichever is configured is checked here.
            .Validate(options => options.Entra is null || IsValid(options.Entra), "PartnerApi:Entra needs a TenantId, a Scope and a ManagedIdentityClientId.")
            .Validate(options => options.LocalTokens is null || IsValid(options.LocalTokens), "PartnerApi:LocalTokens needs an Issuer, an Audience and a SigningKey.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<PartnerApiOptions>, EveryPersonaCanCall>();

        builder.Services.AddSingleton<IPersonaTokens>(services =>
            services.GetRequiredService<IOptions<PartnerApiOptions>>().Value.LocalTokens is null
                ? ActivatorUtilities.CreateInstance<EntraPersonaTokens>(services)
                : ActivatorUtilities.CreateInstance<LocalPersonaTokens>(services));

        // Removing the default handler is still marked for evaluation, and is the one way to replace it for a single client.
#pragma warning disable EXTEXP0001
        builder.Services.AddHttpClient<IPartnerApi, NorviaPartnerApiClient>((services, http) =>
            {
                var options = services.GetRequiredService<IOptions<PartnerApiOptions>>().Value;
                // A relative path resolves under the last segment only when the base address ends in a slash.
                http.BaseAddress = new Uri(options.BaseAddress!.ToString().TrimEnd('/') + "/");
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            // Not the standard handler: its retries and 10-second attempts would send a booking again while the intake
            // Logic App is still retrying the TMS. One attempt, as long as Norvia may take.
            .RemoveAllResilienceHandlers()
            .AddResilienceHandler("norvia-partner-api", (pipeline, context) =>
                pipeline.AddTimeout(context.ServiceProvider.GetRequiredService<IOptions<PartnerApiOptions>>().Value.Timeout));
#pragma warning restore EXTEXP0001
    }

    /// <summary>The Driver app API client, as a Driver calls it. Locally the Simulator signs each Driver's token itself; in Azure it comes from Entra (ADR 0042).</summary>
    private static void AddNorviaDriverApp(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<DriverApiOptions>().BindConfiguration(DriverApiOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => (options.Entra is null) != (options.LocalTokens is null), "Configure either DriverApp:Entra or DriverApp:LocalTokens.")
            // Data annotations don't look inside the nested options, so whichever is configured is checked here.
            .Validate(options => options.Entra is null || IsValid(options.Entra), "DriverApp:Entra needs a TenantId, a Scope, a ManagedIdentityClientId and the DriverClientIds.")
            .Validate(options => options.LocalTokens is null || IsValid(options.LocalTokens), "DriverApp:LocalTokens needs an Issuer, an Audience, a ClientId and a SigningKey.")
            .ValidateOnStart();

        builder.Services.AddSingleton<IDriverTokens>(services =>
            services.GetRequiredService<IOptions<DriverApiOptions>>().Value.LocalTokens is null
                ? ActivatorUtilities.CreateInstance<EntraDriverTokens>(services)
                : ActivatorUtilities.CreateInstance<LocalDriverTokens>(services));

        // The standard resilience handler stays: the Driver app answers a report it has taken already as taken, so a retry changes nothing.
        builder.Services.AddHttpClient<IDriverApp, NorviaDriverAppClient>((services, http) =>
            http.BaseAddress = services.GetRequiredService<IOptions<DriverApiOptions>>().Value.BaseAddress);
    }

    /// <summary>The TMS API client, as the Planner calls it. Locally the Simulator signs its own token, as the TMS UI does; in Azure it is the managed identity's (ADR 0042).</summary>
    private static void AddNorviaTmsApi(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<TmsApiOptions>().BindConfiguration(TmsApiOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => (options.Entra is null) != (options.LocalTokens is null), "Configure either TmsApi:Entra or TmsApi:LocalTokens.")
            // Data annotations don't look inside the nested options, so whichever is configured is checked here.
            .Validate(options => options.Entra is null || IsValid(options.Entra), "TmsApi:Entra needs a Scope and a ManagedIdentityClientId.")
            .Validate(options => options.LocalTokens is null || IsValid(options.LocalTokens), "TmsApi:LocalTokens needs an Issuer, an Audience, a ClientId and a SigningKey.")
            .ValidateOnStart();

        builder.Services.AddSingleton<ITmsTokens>(services =>
            services.GetRequiredService<IOptions<TmsApiOptions>>().Value.LocalTokens is null
                ? ActivatorUtilities.CreateInstance<EntraTmsTokens>(services)
                : ActivatorUtilities.CreateInstance<LocalTmsTokens>(services));

        // The standard resilience handler stays: each call repeats its own idempotency key, so a retry is answered as before.
        builder.Services.AddHttpClient<ITmsApi, NorviaTmsApiClient>((services, http) =>
            http.BaseAddress = services.GetRequiredService<IOptions<TmsApiOptions>>().Value.BaseAddress);
    }
}
