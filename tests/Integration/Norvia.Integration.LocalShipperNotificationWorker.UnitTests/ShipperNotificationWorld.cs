using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalShipperNotificationWorker.UnitTests;

/// <summary>The flow with the Shipper's endpoint and the TMS stood in for, and a clock the test moves.</summary>
internal sealed class ShipperNotificationWorld
{
    public const string ShipperId = "SHP-0001";
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public ShipperNotificationWorld(bool shipperConfigured = true, int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Shipper = new StubHttp(Time);
        Tms = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new ShipperNotificationWorkerOptions
        {
            TmsBaseAddress = new Uri("http://tms.test"),
            TmsTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "shipper-notification-worker", SigningKey = Signing },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        if (shipperConfigured)
        {
            Options.Value.Shippers[ShipperId] = new() { Address = new Uri("http://shipper.test/shipper/v1/SHP-0001/notifications"), ApiKey = "shipper-secret" };
        }

        Flow = new ShipperNotificationFlow(
            new ShipperEndpoint(new HttpClient(Shipper)),
            new TmsReports(new HttpClient(Tms) { BaseAddress = Options.Value.TmsBaseAddress }, Options, Time, NullLogger<TmsReports>.Instance),
            Options, Time, NullLogger<ShipperNotificationFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Shipper { get; }

    public StubHttp Tms { get; }

    public IOptions<ShipperNotificationWorkerOptions> Options { get; }

    public ShipperNotificationFlow Flow { get; }

    public const string NotificationId = "3f8a2b6c-9d41-4c7e-8a15-6b0e2d9f7c05";

    public static string Request(string shipperId = ShipperId) => $$"""
        {
          "eventId": "7d1e0c4a-5b2f-4e8a-9c3d-0a1b2c3d4e05",
          "eventType": "ShipperNotificationRequested",
          "eventVersion": 1,
          "occurredAt": "2030-01-10T08:00:00+00:00",
          "correlationId": "notify-5",
          "notificationId": "{{NotificationId}}",
          "exceptionNumber": "EX-0000005",
          "shipmentNumber": "SH-0000045",
          "transportOrderNumber": "TO-0000005",
          "shipperId": "{{shipperId}}",
          "shipperReference": "BD-2030-0005",
          "deliveryCountry": "PT",
          "deliveryPostcode": "9500-001",
          "exceptionType": "CollectionFailed",
          "message": "The driver could not collect the goods: the dock was closed.",
          "eta": "2030-01-10T10:00:00+00:00",
          "agreedDeliveryWindowEarliest": null,
          "agreedDeliveryWindowLatest": null,
          "planner": "Anna de Vries"
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<ShipperNotificationFlowOutcome> RunAsync(string request)
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
