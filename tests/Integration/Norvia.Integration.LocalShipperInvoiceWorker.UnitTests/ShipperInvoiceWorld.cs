using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.Integration.LocalShipperInvoiceWorker.UnitTests;

/// <summary>The flow with the Shipper's endpoint and the ERP stood in for, and a clock the test moves.</summary>
internal sealed class ShipperInvoiceWorld
{
    public const string ShipperId = "SHP-0001";
    public const string DocumentNumber = "INV-2030-000001";
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public ShipperInvoiceWorld(bool shipperConfigured = true, int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 11, 1, 5, 0, 0, TimeSpan.Zero));
        Shipper = new StubHttp(Time);
        Erp = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new ShipperInvoiceWorkerOptions
        {
            ErpBaseAddress = new Uri("http://erp.test"),
            ErpTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "shipper-invoice-worker", SigningKey = Signing },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        if (shipperConfigured)
        {
            Options.Value.Shippers[ShipperId] = new() { Address = new Uri("http://shipper.test/shipper/v1/SHP-0001/invoices"), ApiKey = "shipper-secret" };
        }

        Flow = new ShipperInvoiceFlow(
            new ShipperEndpoint(new HttpClient(Shipper)),
            new ErpReports(new HttpClient(Erp) { BaseAddress = Options.Value.ErpBaseAddress }, Options, Time, NullLogger<ErpReports>.Instance),
            Options, Time, NullLogger<ShipperInvoiceFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Shipper { get; }

    public StubHttp Erp { get; }

    public IOptions<ShipperInvoiceWorkerOptions> Options { get; }

    public ShipperInvoiceFlow Flow { get; }

    public static string Issued(string shipperId = ShipperId) => $$"""
        {
          "eventId": "0198e7c0-0000-7000-8000-0000000000d1",
          "eventType": "ShipperInvoiceIssued",
          "eventVersion": 1,
          "occurredAt": "2030-11-01T05:00:02+00:00",
          "correlationId": "invoice-1",
          "invoice": {
            "invoiceNumber": "{{DocumentNumber}}",
            "invoiceDate": "2030-11-01",
            "shipper": { "shipperId": "{{shipperId}}", "name": "Brouwer Dranken" },
            "lines": [
              {
                "transportOrderNumber": "TO-0000001",
                "shipperReference": "BD-2030-0001",
                "shipmentNumber": "SH-0000001",
                "deliveryDate": "2030-10-18",
                "chargeLines": [ { "type": "Freight", "amount": 80.00 } ],
                "total": 80.00
              }
            ],
            "currency": "EUR",
            "totalWithoutVat": 80.00,
            "vat": 16.80,
            "totalWithVat": 96.80,
            "issuedAt": "2030-11-01T05:00:02+00:00",
            "correlationId": "invoice-1",
            "status": "Issued"
          }
        }
        """;

    public static string CreditNoteIssued(string shipperId = ShipperId) => $$"""
        {
          "eventId": "0198e7c0-0000-7000-8000-0000000000e1",
          "eventType": "CreditNoteIssued",
          "eventVersion": 1,
          "occurredAt": "2030-11-05T09:30:00+00:00",
          "correlationId": "credit-note-1",
          "creditNote": {
            "creditNoteNumber": "CN-2030-000001",
            "creditNoteDate": "2030-11-05",
            "shipper": { "shipperId": "{{shipperId}}", "name": "Brouwer Dranken" },
            "invoiceNumber": "{{DocumentNumber}}",
            "transportOrderNumber": "TO-0000001",
            "shipperReference": "BD-2030-0001",
            "shipmentNumber": "SH-0000001",
            "deliveryDate": "2030-10-18",
            "currency": "EUR",
            "totalWithoutVat": 14.50,
            "vat": 3.05,
            "totalWithVat": 17.55,
            "reason": "Rate Card error",
            "issuedAt": "2030-11-05T09:30:00+00:00",
            "correlationId": "credit-note-1",
            "status": "Issued"
          }
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<ShipperInvoiceFlowOutcome> RunAsync(string issued)
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
