using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.PartnerPortal.Application.Bookings.Submit;
using Norvia.PartnerPortal.Application.CarrierBookings.AutoAnswer;
using Norvia.PartnerPortal.Application.CarrierShipments.AutoProgress;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Infrastructure;
using Norvia.PartnerPortal.Web.Bookings;
using Norvia.PartnerPortal.Web.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierInvoices;
using Norvia.PartnerPortal.Web.CarrierShipments;
using Norvia.PartnerPortal.Web.Components;
using Norvia.PartnerPortal.Web.Personas;
using Norvia.PartnerPortal.Web.Reset;
using Norvia.PartnerPortal.Web.ShipperInvoices;
using Norvia.PartnerPortal.Web.ShipperNotifications;
using Norvia.PartnerPortal.Web.ShipperTracking;

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
// Carrier Personas that answer automatically do so through this worker, which the Carrier booking endpoint wakes.
builder.Services.AddSingleton<AutoAnswerWorker>();
builder.Services.AddSingleton<IAutoAnswerSignal>(services => services.GetRequiredService<AutoAnswerWorker>());
builder.Services.AddHostedService(services => services.GetRequiredService<AutoAnswerWorker>());
// Carrier Personas in auto-progress send their Tracking events through this worker, which a confirmation wakes.
builder.Services.AddSingleton<ISampleCmr, BundledSampleCmr>();
builder.Services.AddSingleton<AutoProgressWorker>();
builder.Services.AddSingleton<IAutoProgressSignal>(services => services.GetRequiredService<AutoProgressWorker>());
builder.Services.AddHostedService(services => services.GetRequiredService<AutoProgressWorker>());
// A Persona's endpoint without its credential would refuse every caller, so a missing one stops the portal at startup.
builder.Services.AddOptions<CarrierEndpointOptions>().BindConfiguration(CarrierEndpointOptions.SectionName).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<CarrierEndpointOptions>, EveryCarrierHasACredential>();
builder.Services.AddOptions<ResetEndpointOptions>().BindConfiguration(ResetEndpointOptions.SectionName);
builder.Services.AddOptions<ShipperEndpointOptions>().BindConfiguration(ShipperEndpointOptions.SectionName).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ShipperEndpointOptions>, EveryShipperHasACredential>();

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
app.MapCarrierBookingEndpoint();
app.MapCarrierCancellationEndpoint();
app.MapCarrierInvoiceOutcomeEndpoint();
app.MapShipperNotificationEndpoint();
app.MapShipperInvoiceEndpoint();
app.MapShipperInboxEndpoint();
app.MapProofOfDeliveryFileEndpoint();
app.MapResetEndpoints();
app.MapDefaultEndpoints();

await app.RunAsync();
