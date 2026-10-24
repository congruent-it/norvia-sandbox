using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalTrackingWorker.UnitTests;

/// <summary>The flow with the TMS stood in for.</summary>
internal sealed class TrackingWorld
{
    public const string CarrierId = "CAR-0001";
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public TrackingWorld()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Time = time;
        Tms = new StubHttp(time);
        Options = Microsoft.Extensions.Options.Options.Create(new TrackingWorkerOptions
        {
            TmsBaseAddress = new Uri("http://tms.test"),
            TmsTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "tracking-worker", SigningKey = Signing },
        });
        Logs = new FakeLogCollector();
        var logging = new FakeLoggerProvider(Logs);
        var loggers = LoggerFactory.Create(builder => builder.AddProvider(logging));
        Flow = new CarrierTrackingFlow(
            new TmsTrackingEvents(new HttpClient(Tms) { BaseAddress = Options.Value.TmsBaseAddress }, Options, time, loggers.CreateLogger<TmsTrackingEvents>()),
            loggers.CreateLogger<CarrierTrackingFlow>());
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Tms { get; }

    public IOptions<TrackingWorkerOptions> Options { get; }

    public CarrierTrackingFlow Flow { get; }

    public FakeLogCollector Logs { get; }

    public static string Collected => Sample("collected.carrier-tracking.json");

    public static Dictionary<string, object> Properties(string correlationId = "c-1", string partyId = CarrierId, string? sourceMessageId = "E-1") =>
        new Dictionary<string, object?>
        {
            ["correlationId"] = correlationId,
            ["partyId"] = partyId,
            ["sourceMessageId"] = sourceMessageId,
            ["flow"] = "carrier-tracking",
        }.Where(property => property.Value is not null).ToDictionary(property => property.Key, property => property.Value!);

    /// <summary>The keys in scope on the last log with this outcome, as the dashboard receives them.</summary>
    public Dictionary<string, object?> KeysOf(string outcome) =>
        Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary())
            .Last(keys => keys.TryGetValue("outcome", out var value) && Equals(value, outcome));

    private static string Sample(string name)
    {
        using var stream = typeof(TrackingWorld).Assembly.GetManifestResourceStream($"PartnerApi/samples/carrier-tracking/{name}")
            ?? throw new InvalidOperationException($"{name} is not an embedded sample.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
