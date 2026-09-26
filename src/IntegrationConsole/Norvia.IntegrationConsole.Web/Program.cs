using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Search;
using Norvia.IntegrationConsole.Infrastructure;
using Norvia.IntegrationConsole.Web.Components;
using Norvia.IntegrationConsole.Web.Operators;
using Norvia.IntegrationConsole.Web.Trails;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The console keeps nothing of its own. It reads what the estate records, as its own identity (ADR 0017).
builder.AddIntegrationConsoleInfrastructure();
builder.AddOperatorSignIn();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(SearchTrailsQuery)];
});
builder.Services.AddSingleton(TimeProvider.System);
// The estate's own retention, integrationRetentionDays, which caps every search (coding guidelines §7).
builder.Services.AddOptions<IntegrationRetention>().BindConfiguration(IntegrationRetention.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<ITrailRequests, TrailRequests>();

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
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

await app.RunAsync();
