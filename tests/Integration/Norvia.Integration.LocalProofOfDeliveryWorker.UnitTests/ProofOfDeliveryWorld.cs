using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.Integration.LocalProofOfDeliveryWorker.UnitTests;

/// <summary>The flow with the TMS and the inbound container stood in for.</summary>
internal sealed class ProofOfDeliveryWorld
{
    public const string CarrierId = "CAR-0001";
    public const string BlobName = "c-1";
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public ProofOfDeliveryWorld()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2030, 12, 11, 8, 0, 0, TimeSpan.Zero));
        Tms = new StubHttp(time);
        Options = Microsoft.Extensions.Options.Options.Create(new ProofOfDeliveryWorkerOptions
        {
            TmsBaseAddress = new Uri("http://tms.test"),
            TmsTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "proof-of-delivery-worker", SigningKey = Signing },
        });
        Logs = new FakeLogCollector();
        var loggers = LoggerFactory.Create(builder => builder.AddProvider(new FakeLoggerProvider(Logs)));
        Inbound.Blobs[BlobName] = Clean;
        Flow = new CarrierProofOfDeliveryFlow(
            Inbound,
            new TmsProofsOfDelivery(new HttpClient(Tms) { BaseAddress = Options.Value.TmsBaseAddress }, Options, time, loggers.CreateLogger<TmsProofsOfDelivery>()),
            loggers.CreateLogger<CarrierProofOfDeliveryFlow>());
    }

    public StubHttp Tms { get; }

    internal StubInboundProofsOfDelivery Inbound { get; } = new();

    public IOptions<ProofOfDeliveryWorkerOptions> Options { get; }

    internal CarrierProofOfDeliveryFlow Flow { get; }

    public FakeLogCollector Logs { get; }

    public static string Clean => Sample("clean-winter.carrier-proof-of-delivery.json");

    /// <summary>The message body API Management puts on the queue: the blob's reference.</summary>
    public static string Reference(string blobName = BlobName) =>
        new JsonObject { ["container"] = "inbound-proofs-of-delivery", ["blobName"] = blobName }.ToJsonString();

    public static Dictionary<string, object> Properties(string correlationId = "c-1", string partyId = CarrierId, string? sourceMessageId = "VDT-POD-2001") =>
        new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId,
            ["partyId"] = partyId,
            ["sourceMessageId"] = sourceMessageId,
            ["flow"] = "carrier-proof-of-delivery",
        }.Where(property => property.Value is not null).ToDictionary(property => property.Key, property => property.Value!);

    /// <summary>The keys in scope on the last log with this outcome, as the dashboard receives them.</summary>
    public Dictionary<string, object?> KeysOf(string outcome) =>
        Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    private static string Sample(string name)
    {
        using var stream = typeof(ProofOfDeliveryWorld).Assembly.GetManifestResourceStream($"PartnerApi/samples/carrier-proofs-of-delivery/{name}")
            ?? throw new InvalidOperationException($"{name} is not an embedded sample.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
