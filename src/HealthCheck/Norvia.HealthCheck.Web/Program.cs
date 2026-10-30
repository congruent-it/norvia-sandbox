using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Run;
using Norvia.HealthCheck.Infrastructure;
using Norvia.HealthCheck.Web.Agents;
using Norvia.HealthCheck.Web.Checks;
using Norvia.HealthCheck.Web.Components;
using Norvia.HealthCheck.Web.Operators;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The app keeps nothing of its own. It reads the estate's configuration, locally a snapshot of it (#371).
builder.AddHealthCheckInfrastructure();
builder.AddOperatorSignIn();
// Off unless Agents:Enabled. Then the agent API takes the Agents' own tokens beside the operators' sign-in (ADR 0040).
builder.AddAgentApi();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(RunHealthCheckQuery)];
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Checklist.Default);
builder.Services.AddSingleton<RecentReport>();
builder.Services.AddSingleton(new ConsoleLinks(builder.Configuration.GetValue<Uri>("IntegrationConsole:Address")));
builder.Services.AddSingleton<IHealthCheckRequests, HealthCheckRequests>();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

// The agent API answers with status codes and problem details of its own, never the app's page.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments(AgentEndpoints.Prefix, StringComparison.Ordinal),
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

/// <summary>Lets the integration tests start the app.</summary>
public partial class Program;
