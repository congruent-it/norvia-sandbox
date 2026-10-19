using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.Integration.LocalCarrierInvoiceOutcomeWorker.UnitTests;

/// <summary>The flow with the Carrier's endpoint and the ERP stood in for, and a clock the test moves.</summary>
internal sealed class CarrierInvoiceOutcomeWorld
{
    public const string CarrierId = "CAR-0001";
    public const string DocumentNumber = "VDT-2030-0412";
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public CarrierInvoiceOutcomeWorld(bool carrierConfigured = true, int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 11, 1, 5, 0, 0, TimeSpan.Zero));
        Carrier = new StubHttp(Time);
        Erp = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new CarrierInvoiceOutcomeWorkerOptions
        {
            ErpBaseAddress = new Uri("http://erp.test"),
            ErpTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "carrier-invoice-outcome-worker", SigningKey = Signing },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        if (carrierConfigured)
        {
            Options.Value.Carriers[CarrierId] = new() { Address = new Uri("http://carrier.test/carrier/v1/CAR-0001/invoice-outcomes"), ApiKey = "carrier-secret" };
        }

        Flow = new CarrierInvoiceOutcomeFlow(
            new CarrierEndpoint(new HttpClient(Carrier)),
            new ErpReports(new HttpClient(Erp) { BaseAddress = Options.Value.ErpBaseAddress }, Options, Time, NullLogger<ErpReports>.Instance),
            Options, Time, NullLogger<CarrierInvoiceOutcomeFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Carrier { get; }

    public StubHttp Erp { get; }

    public IOptions<CarrierInvoiceOutcomeWorkerOptions> Options { get; }

    public CarrierInvoiceOutcomeFlow Flow { get; }

    public static string Approved(string carrierId = CarrierId) => $$"""
        {
          "eventId": "0198e7c0-0000-7000-8000-0000000000f1",
          "eventType": "CarrierInvoiceApproved",
          "eventVersion": 1,
          "occurredAt": "2030-11-04T09:15:00+00:00",
          "correlationId": "carrier-invoice-1",
          "carrierId": "{{carrierId}}",
          "invoiceNumber": "{{DocumentNumber}}",
          "approvedAmount": 165.50,
          "lines": [
            { "carrierBookingReference": "TRX-998", "result": "Matched", "invoicedAmount": 70.50, "acceptedAmount": 70.50, "remark": null },
            { "carrierBookingReference": "TRX-1000", "result": "Matched", "invoicedAmount": 95.00, "acceptedAmount": 95.00, "remark": null }
          ]
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<CarrierInvoiceOutcomeFlowOutcome> RunAsync(string issued)
    {
        var run = Flow.RunAsync(issued, CancellationToken.None);
        while (!run.IsCompleted)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        return await run;
    }
}
