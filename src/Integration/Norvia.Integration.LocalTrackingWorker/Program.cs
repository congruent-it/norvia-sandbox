using Microsoft.Extensions.Options;
using Norvia.Integration.LocalTrackingWorker;

// Locally there is no Logic Apps runtime, so this plays the Carrier-tracking Logic App (ADR 0031).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<TrackingWorkerOptions>().BindConfiguration(TrackingWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    // Data annotations don't look inside the token options, so they are checked here too.
    .Validate(options => !string.IsNullOrWhiteSpace(options.TmsTokens.Issuer) && !string.IsNullOrWhiteSpace(options.TmsTokens.Audience)
        && !string.IsNullOrWhiteSpace(options.TmsTokens.ClientId) && !string.IsNullOrWhiteSpace(options.TmsTokens.SigningKey),
        "Tracking:TmsTokens needs an Issuer, an Audience, a ClientId and a SigningKey.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// A server error or timeout is retried from the queue, as the workflow's call has no retry policy of its own, so no
// resilience handler wraps the call. Removing the defaults' handler is the only way, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<TmsTrackingEvents>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<TrackingWorkerOptions>>().Value.TmsBaseAddress).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddTransient<CarrierTrackingFlow>();
builder.Services.AddHostedService<TrackingQueueConsumer>();

await builder.Build().RunAsync();
