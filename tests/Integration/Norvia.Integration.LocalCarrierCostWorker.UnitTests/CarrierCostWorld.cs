using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.Integration.LocalCarrierCostWorker.UnitTests;

/// <summary>The flow with the ERP billing API stood in for, and a clock the test moves.</summary>
internal sealed class CarrierCostWorld
{
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public CarrierCostWorld(int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Erp = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new CarrierCostWorkerOptions
        {
            ErpBaseAddress = new Uri("http://erp.test"),
            ErpTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "carrier-cost-worker", SigningKey = Signing },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        Flow = new CarrierCostFlow(
            new ErpCarrierCostApi(new HttpClient(Erp) { BaseAddress = Options.Value.ErpBaseAddress }, Options, Time, NullLogger<ErpCarrierCostApi>.Instance),
            Options, Time, NullLogger<CarrierCostFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Erp { get; }

    public IOptions<CarrierCostWorkerOptions> Options { get; }

    public CarrierCostFlow Flow { get; }

    public static string Incurred(string carrierBookingNumber = "CB-0000042") => $$"""
        {
          "eventId": "0198e7c0-0000-7000-8000-0000000000c1",
          "eventType": "CarrierCostIncurred",
          "eventVersion": 1,
          "occurredAt": "2030-10-02T09:05:00+00:00",
          "correlationId": "carrier-cost-1",
          "carrierBookingNumber": "{{carrierBookingNumber}}",
          "carrierBookingReference": "TRX-8841",
          "shipmentNumber": "SH-0000042",
          "transportOrderNumber": "TO-0000042",
          "carrierId": "CAR-0001",
          "carrierCost": {
            "currency": "EUR",
            "total": 100,
            "chargeLines": [ { "type": "Freight", "amount": 100 } ],
            "source": { "kind": "CarrierRateCard", "rateCardVersion": { "rateCardId": "CRC-0001", "version": 1 } },
            "calculatedAt": "2030-10-01T08:00:00+00:00"
          },
          "deliveryTime": "2030-10-02T10:45:00+02:00"
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<CarrierCostRun> RunAsync(string message)
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
