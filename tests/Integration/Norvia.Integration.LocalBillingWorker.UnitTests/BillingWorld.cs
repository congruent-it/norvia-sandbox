using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.Integration.LocalBillingWorker.UnitTests;

/// <summary>The flow with the ERP billing API stood in for, and a clock the test moves.</summary>
internal sealed class BillingWorld
{
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public BillingWorld(int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Erp = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new BillingWorkerOptions
        {
            ErpBaseAddress = new Uri("http://erp.test"),
            ErpTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "billing-worker", SigningKey = Signing },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        Flow = new BillingFlow(
            new ErpBillingApi(new HttpClient(Erp) { BaseAddress = Options.Value.ErpBaseAddress }, Options, Time, NullLogger<ErpBillingApi>.Instance),
            Options, Time, NullLogger<BillingFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Erp { get; }

    public IOptions<BillingWorkerOptions> Options { get; }

    public BillingFlow Flow { get; }

    public static string Billable(string transportOrderNumber = "TO-0000042") => $$"""
        {
          "eventId": "0198e7c0-0000-7000-8000-0000000000c1",
          "eventType": "TransportOrderBillable",
          "eventVersion": 1,
          "occurredAt": "2030-10-02T09:05:00+00:00",
          "correlationId": "billing-1",
          "intakeCorrelationId": "intake-1",
          "transportOrderNumber": "{{transportOrderNumber}}",
          "shipperId": "SHP-0001",
          "shipperReference": "BD-1",
          "shipmentNumber": "SH-0000042",
          "deliveryTime": "2030-10-02T10:45:00+02:00",
          "freightCharge": { "currency": "EUR", "total": 100, "chargeLines": [ { "type": "Freight", "amount": 100 } ], "rateCardVersion": { "rateCardId": "RC-0001", "version": 1 } },
          "releasedWithoutProofOfDelivery": false
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<BillingRun> RunAsync(string message)
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
