using Microsoft.Extensions.Options;
using Norvia.Integration.LocalCarrierInvoiceOutcomeWorker;

// Locally there is no Logic Apps runtime, so this plays the E2 Carrier invoice outcome Logic App (ADR 0033, ADR 0043).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<CarrierInvoiceOutcomeWorkerOptions>().BindConfiguration(CarrierInvoiceOutcomeWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the Carrier's endpoint under the workflow's own policy, so no second one wraps it. Removing the
// defaults' handler is the only way to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<CarrierEndpoint>(http => http.Timeout = TimeSpan.FromSeconds(30)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddHttpClient<ErpReports>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<CarrierInvoiceOutcomeWorkerOptions>>().Value.ErpBaseAddress);
builder.Services.AddTransient<CarrierInvoiceOutcomeFlow>();
builder.Services.AddHostedService<CarrierInvoiceApprovedConsumer>();

await builder.Build().RunAsync();
