using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.IntegrationConsole.Infrastructure;
using Norvia.IntegrationConsole.Web.Components;
using Norvia.IntegrationConsole.Web.Operators;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The console keeps nothing of its own. It reads what the estate records, as its own identity (ADR 0017).
builder.AddIntegrationConsoleInfrastructure();
builder.AddOperatorSignIn();

builder.Services.AddRazorComponents();
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
app.MapRazorComponents<App>();
app.MapDefaultEndpoints();

await app.RunAsync();
