using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalBookingWorker.UnitTests;

/// <summary>The flow with the Carrier's endpoint and the TMS stood in for, and a clock the test moves.</summary>
internal sealed class BookingWorld
{
    public const string CarrierId = "CARRIER-VAN-DIJK";
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public BookingWorld(bool carrierConfigured = true, int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Carrier = new StubHttp(Time);
        Tms = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new BookingWorkerOptions
        {
            TmsBaseAddress = new Uri("http://tms.test"),
            TmsTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "booking-worker", SigningKey = Signing },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        if (carrierConfigured)
        {
            Options.Value.Carriers[CarrierId] = new() { Address = new Uri("http://carrier.test/carrier/v1/bookings"), ApiKey = "carrier-secret" };
        }

        Flow = new CarrierBookingFlow(
            new CarrierEndpoint(new HttpClient(Carrier)),
            new TmsReports(new HttpClient(Tms) { BaseAddress = Options.Value.TmsBaseAddress }, Options, Time, NullLogger<TmsReports>.Instance),
            Options, Time, NullLogger<CarrierBookingFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Carrier { get; }

    public StubHttp Tms { get; }

    public IOptions<BookingWorkerOptions> Options { get; }

    public CarrierBookingFlow Flow { get; }

    public static string Release(string carrierId = CarrierId) => $$"""
        {
          "eventId": "5c2c7f0e-3a4c-4d0a-9f3e-5a7a1c0c9b11",
          "eventType": "ShipmentReleased",
          "eventVersion": 1,
          "correlationId": "release-1",
          "shipmentNumber": "SH-0000042",
          "transportOrderNumber": "TO-0000007",
          "shipperId": "SHIPPER-ACME",
          "assignedTo": "Carrier",
          "carrierId": "{{carrierId}}",
          "carrierBookingNumber": "CB-0000001",
          "transportOrder": {
            "shipperId": "SHIPPER-ACME",
            "collection": { "address": { "city": "Rotterdam", "country": "NL" }, "window": { "earliest": "2030-10-02T05:30:00+00:00" } },
            "delivery": { "address": { "city": "Düsseldorf", "country": "DE" }, "consignee": { "name": "Getränke Schmitz GmbH" } },
            "goodsLines": [ { "packageType": "pallet", "quantity": 4, "weightKg": 2400 } ]
          }
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<BookingFlowOutcome> RunAsync(string release)
    {
        var run = Flow.RunAsync(release, CancellationToken.None);
        while (!run.IsCompleted)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        return await run;
    }

    public static HttpStatusCode Ok => HttpStatusCode.OK;
}
