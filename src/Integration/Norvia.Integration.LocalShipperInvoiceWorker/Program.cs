using Microsoft.Extensions.Options;
using Norvia.Integration.LocalShipperInvoiceWorker;

// Locally there is no Logic Apps runtime, so this plays the E1 Shipper invoice Logic App (ADR 0033, ADR 0043).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<ShipperInvoiceWorkerOptions>().BindConfiguration(ShipperInvoiceWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the Shipper's endpoint under the workflow's own policy, so no second one wraps it. Removing the
// defaults' handler is the only way to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<ShipperEndpoint>(http => http.Timeout = TimeSpan.FromSeconds(30)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddHttpClient<ErpReports>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<ShipperInvoiceWorkerOptions>>().Value.ErpBaseAddress);
builder.Services.AddTransient<ShipperInvoiceFlow>();
builder.Services.AddHostedService<ShipperInvoiceIssuedConsumer>();

await builder.Build().RunAsync();
