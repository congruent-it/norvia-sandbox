using Microsoft.Extensions.Options;
using Norvia.Integration.LocalPartnerApi;

// Locally there is no API Management and no intake or Carrier-answer Logic App, so this plays them for the Partner portal (ADR 0023).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");

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
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PartnerEdge>();
builder.Services.AddSingleton<ITrackingQueue, ServiceBusTrackingQueue>();
builder.Services.AddHttpClient<TmsIntake>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<TmsOptions>>().Value.BaseAddress);
builder.Services.AddHttpClient<TmsCarrierAnswers>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<TmsOptions>>().Value.BaseAddress);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapSubmitTransportOrder();
app.MapAnswerCarrierBooking();
app.MapSubmitCarrierTracking();

await app.RunAsync();
