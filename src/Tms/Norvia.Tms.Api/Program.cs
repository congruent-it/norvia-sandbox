using System.Text.Json.Serialization;
using Ardalis.Result;
using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Rate;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddScoped<IPipelineBehavior<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>, TrackIntakeInLoggingScope>();
builder.Services.AddScoped<IPipelineBehavior<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>, TriggerRatingAfterAcceptance>();
builder.Services.AddScoped<IPipelineBehavior<RateTransportOrderCommand, Result<RateTransportOrderResult>>, TrackReRateInLoggingScope>();
builder.AddTmsInfrastructure();
builder.AddTmsOutboxPublisher();
builder.AddTmsRatingWorker();

// Issuer, audience and keys come from Authentication:Schemes:Bearer: Entra in Azure, the AppHost's local key otherwise.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Entra puts a managed identity's app roles in the "roles" claim; keep claims as the token names them.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.RoleClaimType = "roles";

        // Configuration switches these checks off when it names no issuer or audience; a TMS that isn't told whom to
        // trust must trust no one.
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
builder.Services.AddOptions<IntakeCallersOptions>().BindConfiguration(IntakeCallersOptions.SectionName)
    .Validate(options => options.ClientIdsByChannel.Values.Any(clientIds => clientIds.Length > 0), "No caller is registered for an intake channel.")
    .Validate(options => options.RegistersEachCallerOnce(), "A caller is registered more than once.")
    .ValidateOnStart();
builder.Services.AddSingleton<IntakeCallers>();

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(SubmitTransportOrderCommand), typeof(TransportOrder)];
});

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // The intake API accepts only the Canonical Model (ADR 0003): anything outside it is refused, not ignored.
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
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

// Outermost, so even a refused or failed call is traced under its correlation ID.
app.UseMiddleware<TrackingScopeMiddleware>();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // A request the API can't bind (malformed JSON, a field outside the Canonical Model) is the caller's error. A change
    // that lost a race, such as two re-rates of one Transport Order, conflicts with the one that won.
    StatusCodeSelector = exception => exception switch
    {
        BadHttpRequestException badRequest => badRequest.StatusCode,
        DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    },
});
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

// API Management imports the OpenAPI document (coding guidelines §6), so it's published in every environment.
app.MapOpenApi().WithDocumentPerVersion();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options => options.WithTitle("Norvia TMS API"));
}
app.MapDefaultEndpoints();
app.MapTransportOrders();

await app.RunAsync();

static bool HasAny(string? one, IEnumerable<string>? many) =>
    !string.IsNullOrWhiteSpace(one) || many?.Any(value => !string.IsNullOrWhiteSpace(value)) == true;
