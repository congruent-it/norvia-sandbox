using Ardalis.Result;
using Mediator;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.DriverApp.Application.Reports.ReportStop;
using Norvia.DriverApp.Application.Reports.StartTrip;
using Norvia.DriverApp.Application.Trips.Receive;
using Norvia.DriverApp.Infrastructure;
using Norvia.DriverApp.Web.Components;
using Norvia.DriverApp.Web.Drivers;
using Norvia.DriverApp.Web.Reports;
using Norvia.DriverApp.Web.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The Driver app keeps its own record of the Trips it was sent, and reaches Norvia only through Azure Integration
// Services (ADR 0001): the Trip dispatch Logic App pushes Trips to its API, and nothing here calls the TMS.
builder.AddDriverAppInfrastructure();
builder.AddDriverAppSeedDrivers();
builder.AddDriverSignIn();
builder.Services.AddScoped<IPipelineBehavior<StartTripCommand, Result<StartTripResult>>, TrackStartTripInLoggingScope>();
builder.Services.AddScoped<IPipelineBehavior<ReportStopCommand, Result<ReportStopResult>>, TrackReportStopInLoggingScope>();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(ReceiveTripVersionCommand)];
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
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
// Every screen is a Driver's own: it needs a token that carries a Driver ID (ADR 0005, ADR 0037).
app.MapRazorComponents<App>().RequireAuthorization(DriverClaims.DriverPolicy);
app.MapDriverSignInStandIn();
app.MapSettings();
app.MapReports();
app.MapDefaultEndpoints();

await app.RunAsync();
