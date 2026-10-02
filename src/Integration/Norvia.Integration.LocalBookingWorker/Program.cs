using Microsoft.Extensions.Options;
using Norvia.Integration.LocalBookingWorker;

// Locally there is no Logic Apps runtime, so this plays the P4 booking Logic App (ADR 0030).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<BookingWorkerOptions>().BindConfiguration(BookingWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the Carrier's endpoint under the workflow's own policy, so no second one wraps it. Removing the
// defaults' handler is the only way to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<CarrierEndpoint>(http => http.Timeout = TimeSpan.FromSeconds(30)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddHttpClient<TmsReports>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<BookingWorkerOptions>>().Value.TmsBaseAddress);
builder.Services.AddTransient<CarrierBookingFlow>();
builder.Services.AddHostedService<ShipmentReleasedConsumer>();

await builder.Build().RunAsync();
