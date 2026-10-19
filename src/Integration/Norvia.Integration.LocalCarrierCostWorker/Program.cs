using Microsoft.Extensions.Options;
using Norvia.Integration.LocalCarrierCostWorker;

// Locally there is no Logic Apps runtime, so this plays the P9 Carrier cost Logic App.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<CarrierCostWorkerOptions>().BindConfiguration(CarrierCostWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the ERP under the workflow's own policy, so no second one wraps it. Removing the defaults' handler is the only way
// to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<ErpCarrierCostApi>((services, http) =>
{
    http.BaseAddress = services.GetRequiredService<IOptions<CarrierCostWorkerOptions>>().Value.ErpBaseAddress;
    http.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddTransient<CarrierCostFlow>();
builder.Services.AddHostedService<CarrierCostConsumer>();

await builder.Build().RunAsync();
