using System.Text.Json.Serialization;
using Ardalis.Result;
using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Norvia.Erp.Api.BillableLines;
using Norvia.Erp.Api.ShipperInvoices;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application.BillableLines.Record;
using Norvia.Erp.Application.CreditNotes.ReportNotDelivered;
using Norvia.Erp.Application.CreditNotes.ReportSent;
using Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;
using Norvia.Erp.Application.ShipperInvoices.ReportSent;
using Norvia.Erp.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddScoped<IPipelineBehavior<RecordBillableLineCommand, Result<RecordBillableLineResult>>, TrackBillableLineInLoggingScope>();
builder.Services.AddScoped<IPipelineBehavior<ReportShipperInvoiceSentCommand, Result<ShipperInvoiceReportResult>>, TrackShipperInvoiceReportInLoggingScope>();
builder.Services.AddScoped<IPipelineBehavior<ReportShipperInvoiceNotDeliveredCommand, Result<ShipperInvoiceReportResult>>, TrackShipperInvoiceNotDeliveredReportInLoggingScope>();
builder.Services.AddScoped<IPipelineBehavior<ReportCreditNoteSentCommand, Result<CreditNoteReportResult>>, TrackCreditNoteReportInLoggingScope>();
builder.Services.AddScoped<IPipelineBehavior<ReportCreditNoteNotDeliveredCommand, Result<CreditNoteReportResult>>, TrackCreditNoteNotDeliveredReportInLoggingScope>();

// The ERP API records and answers; a Not delivered report writes its event to the outbox, and the ERP UI's process publishes it, so the API runs no outbox publisher. The ERP UI loads the master data it checks Shippers against.
builder.AddErpInfrastructure();

// Issuer, audience and keys come from Authentication:Schemes:Bearer: Entra in Azure, the AppHost's local key otherwise.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Entra puts a managed identity's app roles in the "roles" claim; keep claims as the token names them.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.RoleClaimType = "roles";

        // Configuration switches these checks off when it names no issuer or audience; an ERP API that isn't told whom to
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

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(RecordBillableLineCommand)];
});

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // The billing API accepts only the Canonical Model (ADR 0003): anything outside it is refused, not ignored.
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
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

const int DuplicateKeyInUniqueIndex = 2601;
const int DuplicateKeyInConstraint = 2627;

// Outermost, so even a refused or failed call is traced.
app.UseMiddleware<TrackingScopeMiddleware>();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // A request the API can't bind (malformed JSON, a member outside the Canonical Model) is the caller's error. A recording that lost a
    // race with another for the same Transport Order, and again lost the retry, conflicts with the one that won.
    StatusCodeSelector = exception => exception switch
    {
        BadHttpRequestException badRequest => badRequest.StatusCode,
        DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
        DbUpdateException { InnerException: SqlException { Number: DuplicateKeyInUniqueIndex or DuplicateKeyInConstraint } } => StatusCodes.Status409Conflict,
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
    app.MapScalarApiReference(options => options.WithTitle("Norvia ERP API"));
}

app.MapDefaultEndpoints();
app.MapBillableLines();
app.MapShipperInvoices();
app.MapCreditNotes();

await app.RunAsync();

static bool HasAny(string? one, IEnumerable<string>? many) =>
    !string.IsNullOrWhiteSpace(one) || many?.Any(value => !string.IsNullOrWhiteSpace(value)) == true;
