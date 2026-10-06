using Microsoft.Extensions.Options;
using Norvia.Integration.LocalProofOfDeliveryWorker;

// Locally there is no Logic Apps runtime, so this plays the Carrier Proof of delivery Logic App (ADR 0032).
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureServiceBusClient("messaging");
builder.AddAzureBlobServiceClient("blobs");

builder.Services.AddOptions<ProofOfDeliveryWorkerOptions>().BindConfiguration(ProofOfDeliveryWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    // Data annotations don't look inside the token options, so they are checked here too.
    .Validate(options => !string.IsNullOrWhiteSpace(options.TmsTokens.Issuer) && !string.IsNullOrWhiteSpace(options.TmsTokens.Audience)
        && !string.IsNullOrWhiteSpace(options.TmsTokens.ClientId) && !string.IsNullOrWhiteSpace(options.TmsTokens.SigningKey),
        "ProofOfDelivery:TmsTokens needs an Issuer, an Audience, a ClientId and a SigningKey.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IInboundProofsOfDelivery, BlobInboundProofsOfDelivery>();

// A server error or timeout is retried from the queue, as the workflow's call has no retry policy of its own, so no
// resilience handler wraps the call. Removing the defaults' handler is the only way, and it is still marked experimental.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<TmsProofsOfDelivery>((services, http) =>
    http.BaseAddress = services.GetRequiredService<IOptions<ProofOfDeliveryWorkerOptions>>().Value.TmsBaseAddress).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
builder.Services.AddTransient<CarrierProofOfDeliveryFlow>();
builder.Services.AddHostedService<ProofOfDeliveryQueueConsumer>();

await builder.Build().RunAsync();
