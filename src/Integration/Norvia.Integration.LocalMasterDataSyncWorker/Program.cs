using Microsoft.Extensions.Options;
using Norvia.Integration.LocalMasterDataSyncWorker;

// Locally there is no Logic Apps runtime, so this plays the E3 master-data sync Logic App.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<MasterDataSyncWorkerOptions>().BindConfiguration(MasterDataSyncWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the TMS under the workflow's own policy, so no second one wraps it. Removing the defaults' handler is the only way
// to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<TmsMasterDataApi>((services, http) =>
{
    http.BaseAddress = services.GetRequiredService<IOptions<MasterDataSyncWorkerOptions>>().Value.TmsBaseAddress;
    http.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddTransient<MasterDataSyncFlow>();
builder.Services.AddHostedService<MasterDataSyncConsumer>();

await builder.Build().RunAsync();
