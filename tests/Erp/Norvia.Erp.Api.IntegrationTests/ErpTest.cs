using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests;

/// <summary>
/// A test of the ERP. It sets up its own data: the ERP starts it empty (master data aside) with the clock at <see cref="ErpFixture.DefaultNow"/>,
/// and it records the Billable lines it needs through the billing API, as the Billing Logic App does.
/// </summary>
public abstract class ErpTest(ErpFixture fixture) : IAsyncLifetime
{
    protected const string Brouwer = "SHP-0001";
    protected const string Hoeve = "SHP-0002";
    protected const string Groenrijk = "SHP-0003";

    /// <summary>Delivered before <see cref="ErpFixture.DefaultNow"/> and before any 1st the monthly tests run on.</summary>
    protected static readonly DateTimeOffset Delivered = new(2026, 10, 16, 10, 45, 0, TimeSpan.FromHours(2));

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected ErpFixture Fixture { get; } = fixture;

    protected Finance Finance => Fixture.Finance;

    public async ValueTask InitializeAsync() => await Fixture.ResetAsync(Ct);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>The Canonical Model's <c>TransportOrderBillable</c> for a Transport Order; the charge is the freight and, when above zero, the minimum charge adjustment.</summary>
    protected static JsonObject Billable(
        string transportOrderNumber,
        string shipperId = Brouwer,
        decimal freight = 100m,
        decimal minimumChargeAdjustment = 0m,
        DateTimeOffset? deliveryTime = null,
        bool releasedWithoutProofOfDelivery = false)
    {
        var chargeLines = new JsonArray(new JsonObject { ["type"] = "Freight", ["amount"] = freight });
        if (minimumChargeAdjustment > 0)
        {
            chargeLines.Add(new JsonObject { ["type"] = "MinimumChargeAdjustment", ["amount"] = minimumChargeAdjustment });
        }

        return new JsonObject
        {
            ["eventId"] = Guid.NewGuid().ToString(),
            ["eventType"] = "TransportOrderBillable",
            ["eventVersion"] = 1,
            ["occurredAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["intakeCorrelationId"] = Guid.NewGuid().ToString(),
            ["transportOrderNumber"] = transportOrderNumber,
            ["shipperId"] = shipperId,
            ["shipperReference"] = $"REF-{transportOrderNumber}",
            ["shipmentNumber"] = $"SH-{transportOrderNumber[3..]}",
            ["deliveryTime"] = (deliveryTime ?? Delivered).ToString("O", CultureInfo.InvariantCulture),
            ["freightCharge"] = new JsonObject
            {
                ["currency"] = "EUR",
                ["total"] = freight + minimumChargeAdjustment,
                ["chargeLines"] = chargeLines,
                ["rateCardVersion"] = new JsonObject { ["rateCardId"] = "RC-0001", ["version"] = 1 },
            },
            ["releasedWithoutProofOfDelivery"] = releasedWithoutProofOfDelivery,
        };
    }

    /// <summary>Records a Billable line as the Billing Logic App does, and expects the ERP to have taken it for the first time.</summary>
    protected async Task<JsonObject> RecordAsync(JsonObject billable)
    {
        using var client = Fixture.ApiAs(ErpFixture.RecordRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/billable-lines", UriKind.Relative), billable, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!;
    }

    /// <summary>Records one Billable line per Transport Order number for a Shipper, 100 euros each unless given.</summary>
    protected async Task RecordAsync(string shipperId, params string[] transportOrderNumbers)
    {
        foreach (var number in transportOrderNumbers)
        {
            await RecordAsync(Billable(number, shipperId));
        }
    }

    /// <summary>The Shipper invoice Logic App's report on a document, as the identity holding the report role calls.</summary>
    protected async Task<HttpResponseMessage> ReportAsync(string kind, string documentNumber, string outcome, string? correlationId, object? body = null, string[]? roles = null)
    {
        using var client = Fixture.ApiAs(roles ?? [ErpFixture.ReportRole]);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/{kind}/{documentNumber}/{outcome}", UriKind.Relative));
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, Ct);
    }

    protected Task<HttpResponseMessage> ReportInvoiceSentAsync(string invoiceNumber, string correlationId, string[]? roles = null) =>
        ReportAsync("shipper-invoices", invoiceNumber, "report-sent", correlationId, roles: roles);

    protected Task<HttpResponseMessage> ReportInvoiceNotDeliveredAsync(string invoiceNumber, string correlationId, int? statusCode = 503, string message = "The Shipper's invoice endpoint answered 503.") =>
        ReportAsync("shipper-invoices", invoiceNumber, "report-not-delivered", correlationId, new { statusCode, message });

    protected Task<HttpResponseMessage> ReportCreditNoteSentAsync(string creditNoteNumber, string correlationId) =>
        ReportAsync("credit-notes", creditNoteNumber, "report-sent", correlationId);

    protected Task<HttpResponseMessage> ReportCreditNoteNotDeliveredAsync(string creditNoteNumber, string correlationId, int? statusCode = 503, string message = "The Shipper's invoice endpoint answered 503.") =>
        ReportAsync("credit-notes", creditNoteNumber, "report-not-delivered", correlationId, new { statusCode, message });

    /// <summary>Issues a Shipper invoice for the Shipper over the lines recorded, as Finance does, and returns what the ERP shows of it.</summary>
    protected async Task<Application.ShipperInvoices.ShipperInvoiceView> InvoiceAsync(string shipperId)
    {
        var result = await Finance.InvoiceNowAsync(shipperId, Ct);
        result.Refusal.Should().BeNull();
        result.Number.Should().NotBeNull("the Shipper has lines Waiting");
        return await OpenInvoiceAsync(result.Number!);
    }

    protected async Task<Application.ShipperInvoices.ShipperInvoiceView> OpenInvoiceAsync(string invoiceNumber) =>
        (await Finance.AskAsync(new Application.ShipperInvoices.Open.OpenShipperInvoiceQuery(invoiceNumber), Ct))
        ?? throw new InvalidOperationException($"The ERP shows no Shipper invoice {invoiceNumber}.");

    protected Task<IReadOnlyList<Application.ShipperInvoices.ShipperInvoiceView>> InvoicesAsync() =>
        Finance.AskAsync(new Application.ShipperInvoices.List.ListShipperInvoicesQuery(), Ct);

    protected Task<IReadOnlyList<Application.CreditNotes.CreditNoteView>> CreditNotesAsync(string? invoiceNumber = null) =>
        Finance.AskAsync(new Application.CreditNotes.List.ListCreditNotesQuery(invoiceNumber), Ct);

    protected async Task<IReadOnlyList<Application.BillableLines.BillableLineView>> WaitingLinesOfAsync(string shipperId) =>
        (await Finance.AskAsync(new Application.BillableLines.ListWaitingOfShipper.ListWaitingBillableLinesOfShipperQuery(shipperId), Ct))!.Lines;

    /// <summary>The messages of the type the ERP has published, once its outbox has settled.</summary>
    protected async Task<IReadOnlyList<PublishedMessage>> PublishedAsync(string eventType) =>
        [.. (await Fixture.PublishedSettledAsync(Ct)).Where(message => message.EventType == eventType)];

    /// <summary>Sets the clock the ERP sees from now on.</summary>
    protected void Now(DateTimeOffset now) => Fixture.Clock.Set(now);

    protected static async Task<JsonObject> BodyOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!;

    /// <summary>The stable code of a refusal, which the ERP answers a problem with.</summary>
    protected static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await BodyOfAsync(response))["code"]?.GetValue<string>();
}
