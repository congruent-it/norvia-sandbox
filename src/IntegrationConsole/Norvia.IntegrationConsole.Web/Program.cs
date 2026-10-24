using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Search;
using Norvia.IntegrationConsole.Infrastructure;
using Norvia.IntegrationConsole.Web.Agents;
using Norvia.IntegrationConsole.Web.Components;
using Norvia.IntegrationConsole.Web.DeadLetters;
using Norvia.IntegrationConsole.Web.Operators;
using Norvia.IntegrationConsole.Web.Runs;
using Norvia.IntegrationConsole.Web.Trails;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The console keeps nothing of its own. It reads what the estate records, as its own identity (ADR 0017).
builder.AddIntegrationConsoleInfrastructure();
builder.AddOperatorSignIn();
// Off unless Agents:Enabled. Then the agent API takes the Agents' own tokens beside the operators' sign-in (ADR 0040).
builder.AddAgentApi();
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
builder.Services.AddSingleton<IRunRequests, RunRequests>();
builder.Services.AddSingleton<IDeadLetterRequests, DeadLetterRequests>();
builder.Services.AddSingleton<IExplainRequests, ExplainRequests>();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

// The agent API answers with status codes and problem details of its own, never the console's page.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments(AgentTrailEndpoints.Prefix, StringComparison.Ordinal),
    pages => pages.UseStatusCodePagesWithReExecute("/not-found"));
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
if (AgentApiAuthentication.IsEnabled(app.Configuration))
{
    app.MapAgentApi();
}

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

await app.RunAsync();

/// <summary>Lets the integration tests start the console.</summary>
public partial class Program;
