using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.Integration.LocalMasterDataSyncWorker.UnitTests;

/// <summary>The flow with the TMS API stood in for, and a clock the test moves.</summary>
internal sealed class MasterDataSyncWorld
{
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public MasterDataSyncWorld(int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Tms = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new MasterDataSyncWorkerOptions
        {
            TmsBaseAddress = new Uri("http://tms.test"),
            TmsTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "master-data-sync-worker", SigningKey = Signing },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        Flow = new MasterDataSyncFlow(
            new TmsMasterDataApi(new HttpClient(Tms) { BaseAddress = Options.Value.TmsBaseAddress }, Options, Time, NullLogger<TmsMasterDataApi>.Instance),
            Options, Time, NullLogger<MasterDataSyncFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Tms { get; }

    public IOptions<MasterDataSyncWorkerOptions> Options { get; }

    public MasterDataSyncFlow Flow { get; }

    public static string Shipper(string id = "SHP-0001", int revision = 2) => $$"""
        {
          "eventId": "0198e7c0-0000-7000-8000-0000000000e3",
          "eventType": "Shipper",
          "eventVersion": 1,
          "occurredAt": "2030-10-02T09:05:00+00:00",
          "correlationId": "master-data-1",
          "shipper": { "id": "{{id}}", "name": "Brouwer Dranken BV", "active": false, "revision": {{revision}} }
        }
        """;

    public static string Carrier(string id = "CAR-0001", int revision = 2) => $$"""
        {
          "eventId": "0198e7c0-0000-7000-8000-0000000000e4",
          "eventType": "Carrier",
          "eventVersion": 1,
          "occurredAt": "2030-10-02T09:05:00+00:00",
          "correlationId": "master-data-2",
          "carrier": { "id": "{{id}}", "name": "Van Dijk Transport BV", "active": true, "revision": {{revision}} }
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<MasterDataSyncRun> RunAsync(string message)
    {
        var run = Flow.RunAsync(message, CancellationToken.None);
        while (!run.IsCompleted)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        return await run;
    }
}
