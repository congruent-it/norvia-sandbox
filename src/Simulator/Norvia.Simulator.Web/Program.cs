using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Simulator.Application.Runs.Start;
using Norvia.Simulator.Infrastructure;
using Norvia.Simulator.Web;
using Norvia.Simulator.Web.Components;
using Norvia.Simulator.Web.Operators;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The Simulator keeps its own runs and logs, and reaches Norvia only through the Norvia Partner API (ADR 0001).
builder.AddSimulatorInfrastructure();
builder.AddOperatorSignIn();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(StartRunCommand)];
});
builder.Services.AddSingleton<ISimulatorRequests, SimulatorRequests>();
builder.Services.AddSingleton(new ConsoleLinks(builder.Configuration.GetValue<Uri>("IntegrationConsole:Address")));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

var app = builder.Build();

app.UseMiddleware<SimulatorGate>(builder.Configuration.GetValue<bool>("Simulator:Enabled"));

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
