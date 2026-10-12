using Microsoft.Extensions.Options;
using Norvia.Integration.LocalDriverReportsWorker;

// Locally there is no Logic Apps runtime, so this plays the P12 Driver reports Logic App.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<DriverReportsWorkerOptions>().BindConfiguration(DriverReportsWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the TMS and the Driver app under the workflow's own policy, so no second one wraps them. Removing the defaults' handler is the only
// way to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<TmsTripCalls>((services, http) =>
{
    http.BaseAddress = services.GetRequiredService<IOptions<DriverReportsWorkerOptions>>().Value.TmsBaseAddress;
    http.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers();
builder.Services.AddHttpClient<DriverAppOutcomes>((services, http) =>
{
    http.BaseAddress = services.GetRequiredService<IOptions<DriverReportsWorkerOptions>>().Value.DriverApp.BaseAddress;
    http.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddTransient<DriverReportsFlow>();
builder.Services.AddHostedService<DriverReportsConsumer>();

await builder.Build().RunAsync();
