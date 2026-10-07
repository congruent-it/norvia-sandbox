using System.ComponentModel.DataAnnotations;
using Ardalis.SharedKernel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Infrastructure.Data;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;
using Norvia.PartnerPortal.Infrastructure.Personas;
using Polly;

namespace Norvia.PartnerPortal.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "partner-portal";

    public static IHostApplicationBuilder AddPartnerPortalInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddPartnerPortalDbContext();
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped<IConflictRetrier, EfConflictRetrier>();

        builder.Services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => File.Exists(options.Path), "The seed file doesn't exist.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IPersonaDirectory, SeedFilePersonaDirectory>();

        builder.AddNorviaPartnerApi();

        return builder;
    }

    /// <summary>Registers what applying the Partner portal's migrations needs, and nothing else.</summary>
    public static IHostApplicationBuilder AddPartnerPortalDatabaseMigrations(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddPartnerPortalDbContext();
        return builder;
    }

    /// <summary>
    /// Applies pending migrations. Only the Partner portal's migrations run this, as a deployment step, never the portal
    /// itself at startup (coding guidelines §5).
    /// </summary>
    public static async Task MigratePartnerPortalDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PartnerPortalDbContext>().Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// In Azure the database is serverless and pauses after an hour unused, and the first connection then waits while it
    /// resumes, which can take a minute. SqlClient gives up after 15 seconds by default, which would lose the booking's
    /// record.
    /// </summary>
    private static string? WaitingForAResume(string? connectionString)
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

    private static bool IsValid(object options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), validationResults: null, validateAllProperties: true);

    private static void AddPartnerPortalDbContext(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<PartnerPortalDbContext>(options =>
            options.UseSqlServer(WaitingForAResume(builder.Configuration.GetConnectionString(DatabaseConnectionName))));
        builder.EnrichSqlServerDbContext<PartnerPortalDbContext>();
        builder.Services.AddSingleton(TimeProvider.System);
    }

    /// <summary>
    /// The Norvia Partner API client, as the Personas call it. Its tokens come from Entra in Azure, and are signed by the
    /// portal itself locally (ADR 0023): exactly one of the two must be configured.
    /// </summary>
    internal static void AddNorviaPartnerApi(this IHostApplicationBuilder builder)
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
        builder.Services.AddSingleton(_ => new PortalActivities(builder.Environment.ApplicationName));

        // Removing the default handler is still marked for evaluation, and is the one way to replace it for a single client.
#pragma warning disable EXTEXP0001
        builder.Services.AddHttpClient<INorviaPartnerApi, NorviaPartnerApiClient>((services, http) =>
            {
                var options = services.GetRequiredService<IOptions<PartnerApiOptions>>().Value;
                // A relative path resolves under the last segment only when the base address ends in a slash.
                http.BaseAddress = new Uri(options.BaseAddress!.ToString().TrimEnd('/') + "/");
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            // Not the standard handler: its retries and 10-second attempts would send a booking again while the intake
            // Logic App is still retrying the TMS. One attempt, as long as Norvia may take; the presenter sends it again.
            .RemoveAllResilienceHandlers()
            .AddResilienceHandler("norvia-partner-api", (pipeline, context) =>
                pipeline.AddTimeout(context.ServiceProvider.GetRequiredService<IOptions<PartnerApiOptions>>().Value.Timeout));
#pragma warning restore EXTEXP0001
    }
}
