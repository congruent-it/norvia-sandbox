using Microsoft.Extensions.Options;
using Norvia.Integration.LocalBillingWorker;

// Locally there is no Logic Apps runtime, so this plays the E1 Billing Logic App.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<BillingWorkerOptions>().BindConfiguration(BillingWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the ERP under the workflow's own policy, so no second one wraps it. Removing the defaults' handler is the only way
// to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<ErpBillingApi>((services, http) =>
{
    http.BaseAddress = services.GetRequiredService<IOptions<BillingWorkerOptions>>().Value.ErpBaseAddress;
    http.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddTransient<BillingFlow>();
builder.Services.AddHostedService<BillingConsumer>();

await builder.Build().RunAsync();
