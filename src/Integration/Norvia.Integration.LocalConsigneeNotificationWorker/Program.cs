using Microsoft.Extensions.Options;
using Norvia.Integration.LocalConsigneeNotificationWorker;

// Locally there is no Logic Apps runtime, so this plays the P7 Consignee notification Logic App (ADR 0033).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

builder.Services.AddOptions<ConsigneeNotificationWorkerOptions>().BindConfiguration(ConsigneeNotificationWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the TMS call under the workflow's own policy, so no second one wraps it. Removing the defaults' handler is the only way
// to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<TmsConsigneeNotifications>((services, http) =>
{
    http.BaseAddress = services.GetRequiredService<IOptions<ConsigneeNotificationWorkerOptions>>().Value.TmsBaseAddress;
    http.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddSingleton<IConsigneeMailbox, SmtpConsigneeMailbox>();
builder.Services.AddTransient<ConsigneeNotificationFlow>();
builder.Services.AddHostedService<ConsigneeNotificationRequestedConsumer>();

await builder.Build().RunAsync();
