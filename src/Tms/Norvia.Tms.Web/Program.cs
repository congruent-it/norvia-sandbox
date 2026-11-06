using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure;
using Norvia.Tms.Infrastructure.Agents;
using Norvia.Tms.Web.Agents;
using Norvia.Tms.Web.Components;
using Norvia.Tms.Web.Roles;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.TmsApi;
using Norvia.Tms.Web.TransportOrders;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The TMS UI reads through the TMS query side, in process. It changes things only through the TMS API, as its own
// identity, and takes in nothing and publishes nothing: the TMS API does, and runs the outbox publisher (ADR 0028).
builder.AddTmsInfrastructure();
builder.AddAgentClient();
builder.AddTmsProofOfDeliveryStorage();
builder.AddTmsResetUnavailable();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(ListTransportOrdersQuery), typeof(TransportOrder)];
});
builder.Services.AddSingleton<ITransportOrderQueries, TransportOrderQueries>();
builder.Services.AddSingleton<IExplainRequests, ExplainRequests>();
builder.AddTmsApi();
builder.Services.AddOptions<LiveListOptions>().BindConfiguration(LiveListOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapRoleSwitcher();
app.MapProofOfDeliveryFiles();
app.MapDefaultEndpoints();

await app.RunAsync();
