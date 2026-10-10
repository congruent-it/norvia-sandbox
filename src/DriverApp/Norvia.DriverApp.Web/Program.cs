using Mediator;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.DriverApp.Application.Trips.Receive;
using Norvia.DriverApp.Domain.Trips;
using Norvia.DriverApp.Infrastructure;
using Norvia.DriverApp.Web.Components;
using Norvia.DriverApp.Web.Sessions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The Driver app keeps its own record of the Trips it was sent, and reaches Norvia only through Azure Integration
// Services (ADR 0001): the Trip dispatch Logic App pushes Trips to its API, and nothing here calls the TMS.
builder.AddDriverAppInfrastructure();
builder.AddDriverAppSeedDrivers();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(ReceiveTripVersionCommand), typeof(DriverTrip)];
});

// Static server-side rendering throughout: the screens read, and the changes are form posts.
builder.Services.AddRazorComponents();
builder.Services.AddFluentUIComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();
app.MapSession();
app.MapDefaultEndpoints();

await app.RunAsync();
