using Microsoft.Extensions.Options;
using Norvia.Integration.LocalCarrierInvoiceWorker;

// Locally there is no Logic Apps runtime, so this plays the E2 Carrier invoice Logic App behind its HTTP trigger (ADR 0023, ADR 0043).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<CarrierInvoiceWorkerOptions>().BindConfiguration(CarrierInvoiceWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

// The flow retries the ERP under the workflow's own policy, so no second one wraps it. Removing the defaults' handler is the only way
// to do that, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<ErpCarrierInvoices>((services, http) =>
{
    http.BaseAddress = services.GetRequiredService<IOptions<CarrierInvoiceWorkerOptions>>().Value.ErpBaseAddress;
    http.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddTransient<CarrierInvoiceFlow>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapCarrierInvoiceTrigger();

await app.RunAsync();
