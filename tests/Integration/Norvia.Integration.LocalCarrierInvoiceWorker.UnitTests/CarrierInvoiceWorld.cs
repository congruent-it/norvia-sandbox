using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.Integration.LocalCarrierInvoiceWorker.UnitTests;

/// <summary>The flow with the ERP billing API stood in for, and a clock the test moves.</summary>
internal sealed class CarrierInvoiceWorld
{
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";
    public const string CorrelationId = "1c7b0d3e-5a54-4f6a-9d5f-6a2f3c0d9a11";

    public CarrierInvoiceWorld(int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Erp = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new CarrierInvoiceWorkerOptions
        {
            ErpBaseAddress = new Uri("http://erp.test"),
            ErpTokens = new() { Issuer = "issuer", Audience = "erp", ClientId = "carrier-invoice-worker", SigningKey = Signing },
            Trigger = new() { Issuer = "issuer", Audience = "trigger", ClientId = "api-management", SigningKey = Signing },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        Flow = new CarrierInvoiceFlow(
            new ErpCarrierInvoices(new HttpClient(Erp) { BaseAddress = Options.Value.ErpBaseAddress }, Options, Time, NullLogger<ErpCarrierInvoices>.Instance),
            Options, Time, NullLogger<CarrierInvoiceFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Erp { get; }

    public IOptions<CarrierInvoiceWorkerOptions> Options { get; }

    public CarrierInvoiceFlow Flow { get; }

    public static string Sample(string name)
    {
        using var stream = typeof(CarrierInvoiceWorld).Assembly.GetManifestResourceStream($"PartnerApi/samples/carrier-invoices/{name}")
            ?? throw new InvalidOperationException($"{name} is not an embedded sample.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>What the ERP answers for a Carrier invoice it recorded.</summary>
    public static string ErpReceived(string status = "Received", string result = "Matched") => $$"""
        {
          "invoice": {
            "invoiceNumber": "VDT-2026-0412",
            "status": "{{status}}",
            "approvedAmount": null,
            "lines": [
              { "carrierBookingReference": "TRX-998", "amount": 70.50, "result": "{{result}}" },
              { "carrierBookingReference": "TRX-999", "amount": 120.00, "result": "{{result}}" }
            ]
          },
          "repeat": false
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> run)
    {
        var running = run();
        while (!running.IsCompleted)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        return await running;
    }
}
