using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Norvia.Integration.BookingDraft;
using Norvia.Integration.BookingDraft.Api;
using Norvia.Integration.BookingDraft.Api.Foundry;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOptions<BookingDraftHostOptions>().BindConfiguration(BookingDraftHostOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => options.Api.Issuer.Length > 0 && options.Api.Audience.Length > 0 && options.Api.GatewayClientId.Length > 0, "Agents:Api needs an Issuer, an Audience and a GatewayClientId.")
    .Validate(options => !options.Enabled || options.Foundry.ProjectEndpoint is { IsAbsoluteUri: true }, "Agents:Foundry:ProjectEndpoint must be the Intake project's absolute address.")
    .ValidateOnStart();
builder.AddGatewayAuthentication();

// The host asks Foundry as its own identity. Locally (never started by the AppHost) a developer's own sign-in does.
// Aspire gives the container app its identity and puts the client ID in AZURE_CLIENT_ID.
builder.Services.AddSingleton<TokenCredential>(_ =>
    builder.Configuration["AZURE_CLIENT_ID"] is { Length: > 0 } clientId
        ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
        : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true, ExcludeWorkloadIdentityCredential = true }));
builder.Services.AddSingleton<BookingDraftActivities>();

// A short timeout and no retry: the client has its own deadline, and the standard resilience handler would retry (ADR 0040).
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is the only way to opt a client out of the defaults.
builder.Services.AddHttpClient<IAgentClient, FoundryAgentClient>(http => http.Timeout = Timeout.InfiniteTimeSpan)
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddSingleton(services => new BookingDraftExtractor(services.GetRequiredService<IAgentClient>()));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapBookingDraft();
app.Run();

/// <summary>For the tests' host.</summary>
public partial class Program;
