using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Erp.Application.MasterData.Shippers.List;
using Norvia.Erp.Infrastructure;
using Norvia.Erp.Web.Live;
using Norvia.Erp.Web.Operators;
using Norvia.Erp.Web.ShipperInvoices;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The ERP keeps its own database and outbox, and loads its master data from the seed file as it starts. Only Finance reaches its UI.
builder.AddErpInfrastructure();
builder.AddErpSeedLoading();
builder.AddErpOutboxPublisher();
builder.AddFinanceSignIn();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(ListShippersQuery)];
});

builder.Services.AddSingleton<ErpActivities>();
builder.Services.AddScoped<InvoiceNowAction>();
builder.Services.AddSingleton<MonthlyInvoiceRun>();
builder.Services.AddHostedService<MonthlyInvoiceRunWorker>();
builder.Services.AddOptions<LiveListOptions>().BindConfiguration(LiveListOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<Norvia.Erp.Web.Components.App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

await app.RunAsync();
