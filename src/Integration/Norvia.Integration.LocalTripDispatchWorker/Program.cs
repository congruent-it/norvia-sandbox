using Microsoft.Extensions.Options;
using Norvia.Integration.LocalTripDispatchWorker;

// Locally there is no Logic Apps runtime, so this plays the P11 Trip dispatch Logic App.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<TripDispatchWorkerOptions>().BindConfiguration(TripDispatchWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the Driver app under the workflow's own policy, so no second one wraps it. Removing the
// defaults' handler is the only way to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<DriverAppEndpoint>(http => http.Timeout = TimeSpan.FromSeconds(30)).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddHttpClient<TmsReports>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<TripDispatchWorkerOptions>>().Value.TmsBaseAddress);
builder.Services.AddTransient<TripDispatchFlow>();
builder.Services.AddHostedService<TripDispatchConsumer>();

await builder.Build().RunAsync();
