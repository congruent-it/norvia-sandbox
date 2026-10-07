using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierCancellationWorker.UnitTests;

/// <summary>The flow with the Carrier's endpoint and the TMS stood in for, and a clock the test moves.</summary>
internal sealed class CarrierCancellationWorld
{
    public const string CarrierId = "CAR-0001";
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public CarrierCancellationWorld(bool carrierConfigured = true, int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Carrier = new StubHttp(Time);
        Tms = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new CarrierCancellationWorkerOptions
        {
            TmsBaseAddress = new Uri("http://tms.test"),
            TmsTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "carrier-cancellation-worker", SigningKey = Signing },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        if (carrierConfigured)
        {
            Options.Value.Carriers[CarrierId] = new() { Address = new Uri("http://carrier.test/carrier/v1/CAR-0001/cancellations"), ApiKey = "carrier-secret" };
        }

        Flow = new CarrierCancellationFlow(
            new CarrierEndpoint(new HttpClient(Carrier)),
            new TmsReports(new HttpClient(Tms) { BaseAddress = Options.Value.TmsBaseAddress }, Options, Time, NullLogger<TmsReports>.Instance),
            Options, Time, NullLogger<CarrierCancellationFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Carrier { get; }

    public StubHttp Tms { get; }

    public IOptions<CarrierCancellationWorkerOptions> Options { get; }

    public CarrierCancellationFlow Flow { get; }

    public const string CarrierBookingNumber = "CB-0000041";

    public static string Cancelled(string carrierId = CarrierId) => $$"""
        {
          "eventId": "9b2c1d3e-4f5a-4b6c-8d7e-1a2b3c4d5e01",
          "eventType": "CarrierBookingCancelled",
          "eventVersion": 1,
          "occurredAt": "2030-10-02T09:00:00+00:00",
          "correlationId": "cancel-1",
          "carrierBookingNumber": "{{CarrierBookingNumber}}",
          "carrierBookingReference": "KESSLER-88213",
          "shipmentNumber": "SH-0000041",
          "carrierId": "{{carrierId}}",
          "exceptionNumber": "EX-0000001",
          "reasonText": "The ETA is far beyond the delivery window."
        }
        """;

    public static string Withdrawn(string carrierId = CarrierId) => $$"""
        {
          "eventId": "9b2c1d3e-4f5a-4b6c-8d7e-1a2b3c4d5e02",
          "eventType": "CarrierBookingWithdrawn",
          "eventVersion": 1,
          "occurredAt": "2030-10-02T09:30:00+00:00",
          "correlationId": "withdraw-1",
          "carrierBookingNumber": "{{CarrierBookingNumber}}",
          "shipmentNumber": "SH-0000041",
          "carrierId": "{{carrierId}}"
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<CarrierCancellationFlowOutcome> RunAsync(string request)
    {
        var run = Flow.RunAsync(request, CancellationToken.None);
        while (!run.IsCompleted)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        return await run;
    }

    public static HttpStatusCode Ok => HttpStatusCode.OK;
}
