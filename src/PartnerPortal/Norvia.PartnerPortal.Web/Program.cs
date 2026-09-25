using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.PartnerPortal.Application.Bookings.Submit;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Infrastructure;
using Norvia.PartnerPortal.Web.Bookings;
using Norvia.PartnerPortal.Web.Components;
using Norvia.PartnerPortal.Web.Personas;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The Partner portal plays every party outside Norvia. It reaches Norvia only through the Norvia Partner API, and keeps
// its own record of what each Persona sent (ADR 0001).
builder.AddPartnerPortalInfrastructure();
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(SubmitBookingCommand), typeof(Booking)];
});
builder.Services.AddSingleton<IPartnerPortal, PartnerPortalRequests>();

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
app.MapPersonaSwitcher();
app.MapDefaultEndpoints();

await app.RunAsync();
