using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Tms.Application.TransportOrders.List;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure;
using Norvia.Tms.Web.Components;
using Norvia.Tms.Web.TransportOrders;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The TMS UI reads through the TMS query side, in process. It takes in nothing and publishes nothing: the TMS API
// does, and runs the outbox publisher.
builder.AddTmsInfrastructure();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(ListTransportOrdersQuery), typeof(TransportOrder)];
});
builder.Services.AddSingleton<ITransportOrderQueries, TransportOrderQueries>();
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
app.MapDefaultEndpoints();

await app.RunAsync();
