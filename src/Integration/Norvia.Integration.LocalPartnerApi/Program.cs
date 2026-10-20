using Microsoft.Extensions.Options;
using Norvia.Integration.LocalPartnerApi;

// Locally there is no API Management and no intake or Carrier-answer Logic App, so this plays them for the Partner portal (ADR 0023).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");
builder.AddAzureBlobServiceClient("blobs");

// Data annotations don't look inside the token options, so they are checked here too.
builder.Services.AddOptions<PartnerApiOptions>().BindConfiguration(PartnerApiOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => options.Tokens.IsComplete, "PartnerApi:Tokens needs an Issuer, an Audience and a SigningKey.")
    .ValidateOnStart();
builder.Services.AddOptions<TmsOptions>().BindConfiguration(TmsOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => options.Tokens.IsComplete, "Tms:Tokens needs an Issuer, an Audience and a SigningKey.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Tokens.ClientId), "Tms:Tokens:ClientId must name the intake Logic App's client.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AnswerClientId), "Tms:AnswerClientId must name the Carrier-answer Logic App's client.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ShipperTrackingClientId), "Tms:ShipperTrackingClientId must name API Management's client.")
    .ValidateOnStart();
builder.Services.AddOptions<CarrierInvoiceOptions>().BindConfiguration(CarrierInvoiceOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => options.Tokens.IsComplete, "CarrierInvoice:Tokens needs an Issuer, an Audience and a SigningKey.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Tokens.ClientId), "CarrierInvoice:Tokens:ClientId must name API Management's client.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PartnerEdge>();
builder.Services.AddSingleton<TrackingRateLimit>();
builder.Services.AddSingleton<ShipperTrackingMap>();
builder.Services.AddSingleton<ITrackingQueue, ServiceBusTrackingQueue>();
builder.Services.AddSingleton<IInboundProofsOfDelivery, BlobInboundProofsOfDelivery>();
builder.Services.AddSingleton<IProofOfDeliveryQueue, ServiceBusProofOfDeliveryQueue>();
builder.Services.AddHttpClient<TmsIntake>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<TmsOptions>>().Value.BaseAddress);
builder.Services.AddHttpClient<TmsCarrierAnswers>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<TmsOptions>>().Value.BaseAddress);
// A short wait and no retry, as the Shipper tracking policy has: a failure reaches the Shipper at once (P8). The service
// defaults' standard resilience handler would retry, so this client drops it.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is the only way to opt a client out of the defaults.
builder.Services.AddHttpClient<TmsShipperTracking>((services, http) =>
    {
        http.BaseAddress = services.GetRequiredService<IOptions<TmsOptions>>().Value.BaseAddress;
        http.Timeout = TmsShipperTracking.Timeout;
    })
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

// The Logic App retries the ERP itself and answers the Carrier at once, so the call neither retries nor times out early: API Management
// doesn't either. The workflow's own retries can run for about a minute.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<CarrierInvoiceLogicApp>((services, http) =>
    {
        http.BaseAddress = services.GetRequiredService<IOptions<CarrierInvoiceOptions>>().Value.BaseAddress;
        http.Timeout = TimeSpan.FromMinutes(2);
    })
    .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapSubmitTransportOrder();
app.MapAnswerCarrierBooking();
app.MapSubmitCarrierInvoice();
app.MapSubmitCarrierTracking();
app.MapSubmitCarrierProofOfDelivery();
app.MapShipperTrackingQueries();

await app.RunAsync();
