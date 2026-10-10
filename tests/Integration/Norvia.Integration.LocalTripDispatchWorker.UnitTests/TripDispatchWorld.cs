using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalTripDispatchWorker.UnitTests;

/// <summary>The flow with the Driver app and the TMS stood in for, and a clock the test moves.</summary>
internal sealed class TripDispatchWorld
{
    public const string TripNumber = "TR-0000007";
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public TripDispatchWorld(int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        DriverApp = new StubHttp(Time);
        Tms = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new TripDispatchWorkerOptions
        {
            TmsBaseAddress = new Uri("http://tms.test"),
            TmsTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "trip-dispatch-worker", SigningKey = Signing },
            DriverApp = new() { Address = new Uri("http://driver-app.test/v1/trips"), ApiKey = "driver-app-secret" },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });

        Flow = new TripDispatchFlow(
            new DriverAppEndpoint(new HttpClient(DriverApp)),
            new TmsReports(new HttpClient(Tms) { BaseAddress = Options.Value.TmsBaseAddress }, Options, Time, NullLogger<TmsReports>.Instance),
            Options, Time, NullLogger<TripDispatchFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp DriverApp { get; }

    public StubHttp Tms { get; }

    public IOptions<TripDispatchWorkerOptions> Options { get; }

    public TripDispatchFlow Flow { get; }

    public static string Dispatched(int version = 2) => $$"""
        {
          "eventId": "0197a0c2-5b1e-7c3a-9d4e-2f6b8a1c3e51",
          "eventType": "TripDispatched",
          "eventVersion": 1,
          "occurredAt": "2030-10-02T09:05:00+00:00",
          "correlationId": "dispatch-1",
          "tripNumber": "{{TripNumber}}",
          "version": {{version}},
          "day": "2030-10-03",
          "plannedStart": "08:00:00",
          "plannedDrivingDuration": "PT4H30M",
          "vehicle": { "vehicleId": "VEH-0001", "registration": "TT-101-V", "vehicleType": "Van" },
          "driver": { "driverId": "DRV-0002", "name": "Fatima El Amrani" },
          "stops": []
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<TripDispatchFlowOutcome> RunAsync(string message)
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
