using Microsoft.Extensions.Options;
using Norvia.Integration.LocalShipperNotificationWorker;

// Locally there is no Logic Apps runtime, so this plays the P7 Shipper notification Logic App (ADR 0033).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<ShipperNotificationWorkerOptions>().BindConfiguration(ShipperNotificationWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the Shipper's endpoint under the workflow's own policy, so no second one wraps it. Removing the
// defaults' handler is the only way to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<ShipperEndpoint>(http => http.Timeout = TimeSpan.FromSeconds(30)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddHttpClient<TmsReports>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<ShipperNotificationWorkerOptions>>().Value.TmsBaseAddress);
builder.Services.AddTransient<ShipperNotificationFlow>();
builder.Services.AddHostedService<ShipperNotificationRequestedConsumer>();

await builder.Build().RunAsync();
