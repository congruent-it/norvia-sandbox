using System.Text.Json.Serialization;
using Ardalis.Result;
using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Norvia.DriverApp.Api.Reports;
using Norvia.DriverApp.Api.Settings;
using Norvia.DriverApp.Api.Trips;
using Norvia.DriverApp.Application.Reports.EndTrip;
using Norvia.DriverApp.Application.Reports.ReportOutcome;
using Norvia.DriverApp.Application.Reports.StartTrip;
using Norvia.DriverApp.Application.Trips.Receive;
using Norvia.DriverApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDriverAppInfrastructure();
builder.AddDriverAppSeedDrivers();
// The API stores no file, but its mediator holds every handler of the Application, so it has the port the Deliver handler needs.
builder.AddDriverAppProofOfDeliveryTransit();
builder.AddDriverAppOutboxPublisher();

// The outcome endpoint takes the Driver reports Logic App's token. Issuer, audience and keys come from Authentication:Schemes:Bearer: Entra in
// Azure, the AppHost's local key otherwise.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Entra puts a managed identity's app roles in the "roles" claim; keep claims as the token names them.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.RoleClaimType = "roles";

        // Configuration switches these checks off when it names no issuer or audience; a Driver app that isn't told whom to trust must trust no one.
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
    });
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Validate(
        options => HasAny(options.TokenValidationParameters.ValidIssuer, options.TokenValidationParameters.ValidIssuers)
            && HasAny(options.TokenValidationParameters.ValidAudience, options.TokenValidationParameters.ValidAudiences)
            && (options.Authority is not null || options.TokenValidationParameters.IssuerSigningKeys?.Any() == true),
        "Authentication:Schemes:Bearer must name the issuer and audience to trust, and an Authority or signing keys.")
    .ValidateOnStart();
builder.Services.AddAuthorization();

builder.Services.AddOptions<DriverEndpointOptions>().BindConfiguration(DriverEndpointOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<IPipelineBehavior<ReceiveTripVersionCommand, Result<TripVersionOutcome>>, TrackTripVersionInLoggingScope>();
builder.Services.AddScoped<IPipelineBehavior<StartTripCommand, Result<StartTripResult>>, TrackStartTripInLoggingScope>();
builder.Services.AddScoped<IPipelineBehavior<EndTripCommand, Result<EndTripResult>>, TrackEndTripInLoggingScope>();
builder.Services.AddScoped<IPipelineBehavior<ReportOutcomeCommand, Result<ReportOutcomeResult>>, TrackReportOutcomeInLoggingScope>();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(ReceiveTripVersionCommand)];
});

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1);
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    })
    .AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // A request the API can't bind (malformed JSON, a missing field, a Stop kind outside the Canonical Model) is the caller's error.
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

// API Management imports the OpenAPI document (coding guidelines §6), so it's published in every environment.
app.MapOpenApi().WithDocumentPerVersion();
app.MapDefaultEndpoints();
app.MapTrips();
app.MapReportOutcomes();
app.MapSettings();

await app.RunAsync();

static bool HasAny(string? single, IEnumerable<string>? several) => !string.IsNullOrEmpty(single) || several?.Any(value => !string.IsNullOrEmpty(value)) == true;
