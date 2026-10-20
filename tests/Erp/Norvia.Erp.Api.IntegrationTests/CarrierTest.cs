using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Erp.Application.CarrierCosts.ListOpen;
using Norvia.Erp.Application.CarrierInvoices.List;

namespace Norvia.Erp.Api.IntegrationTests;

/// <summary>
/// A test of Carrier invoice matching (E2). It records its Carrier costs and receives its Carrier invoices through the billing API, as the Carrier cost and
/// Carrier invoice Logic Apps do, and reads what the ERP shows Finance.
/// </summary>
public abstract class CarrierTest(ErpFixture fixture) : ErpTest(fixture)
{
    protected const string Carrier = "CAR-0001";
    protected const string OtherCarrier = "CAR-0002";

    private static string Iso(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>The Canonical Model's <c>CarrierCostIncurred</c> for a Carrier booking; a Spot price, or a Carrier Rate Card version when <paramref name="rateCard"/>.</summary>
    protected static JsonObject CarrierCost(string bookingNumber, string reference, decimal total, string carrier = Carrier, bool rateCard = false)
    {
        var number = int.Parse(bookingNumber[3..], CultureInfo.InvariantCulture);
        return new JsonObject
        {
            ["eventId"] = Guid.NewGuid().ToString(),
            ["eventType"] = "CarrierCostIncurred",
            ["eventVersion"] = 1,
            ["occurredAt"] = Iso(DateTimeOffset.UtcNow),
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["carrierBookingNumber"] = bookingNumber,
            ["carrierBookingReference"] = reference,
            ["shipmentNumber"] = $"SH-{number:D7}",
            ["transportOrderNumber"] = $"TO-{number:D7}",
            ["carrierId"] = carrier,
            ["carrierCost"] = new JsonObject
            {
                ["currency"] = "EUR",
                ["total"] = total,
                ["chargeLines"] = new JsonArray(new JsonObject { ["type"] = "Freight", ["amount"] = total }),
                ["source"] = rateCard
                    ? new JsonObject { ["kind"] = "CarrierRateCard", ["rateCardVersion"] = new JsonObject { ["rateCardId"] = "CRC-0001", ["version"] = 2 } }
                    : new JsonObject { ["kind"] = "Spot", ["remark"] = "Spot price agreed by phone." },
                ["calculatedAt"] = Iso(DateTimeOffset.UtcNow),
            },
            ["deliveryTime"] = Iso(Delivered),
        };
    }

    /// <summary>The Canonical Model's Carrier invoice, as the Carrier invoice Logic App sends it.</summary>
    protected static JsonObject CarrierInvoice(string carrier, string invoiceNumber, params (string Reference, decimal Amount)[] lines) => new()
    {
        ["carrierId"] = carrier,
        ["invoiceNumber"] = invoiceNumber,
        ["invoiceDate"] = "2026-10-19",
        ["correlationId"] = Guid.NewGuid().ToString(),
        ["lines"] = new JsonArray([.. lines.Select(line => (JsonNode)new JsonObject { ["carrierBookingReference"] = line.Reference, ["amount"] = line.Amount })]),
    };

    /// <summary>Calls Record a Carrier cost as the identity holding the roles.</summary>
    protected async Task<HttpResponseMessage> PostCostAsync(JsonObject cost, params string[] roles)
    {
        using var client = Fixture.ApiAs(roles.Length > 0 ? roles : [ErpFixture.RecordCarrierCostRole]);
        return await client.PostAsJsonAsync(new Uri("/v1/carrier-costs", UriKind.Relative), cost, Ct);
    }

    /// <summary>Calls Receive a Carrier invoice as the identity holding the roles.</summary>
    protected async Task<HttpResponseMessage> PostInvoiceAsync(JsonObject invoice, params string[] roles)
    {
        using var client = Fixture.ApiAs(roles.Length > 0 ? roles : [ErpFixture.ReceiveCarrierInvoiceRole]);
        return await client.PostAsJsonAsync(new Uri("/v1/carrier-invoices", UriKind.Relative), invoice, Ct);
    }

    /// <summary>Records a Spot Carrier cost for a Carrier booking and expects the ERP to have taken it for the first time.</summary>
    protected async Task RecordCostAsync(string bookingNumber, string reference, decimal total, string carrier = Carrier)
    {
        using var response = await PostCostAsync(CarrierCost(bookingNumber, reference, total, carrier));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Receives a Carrier invoice and expects the ERP to have taken it for the first time; returns the invoice as the ERP answered with it.</summary>
    protected async Task<JsonObject> ReceiveAsync(string invoiceNumber, params (string Reference, decimal Amount)[] lines) =>
        await ReceiveAsync(Carrier, invoiceNumber, lines);

    protected async Task<JsonObject> ReceiveAsync(string carrier, string invoiceNumber, params (string Reference, decimal Amount)[] lines)
    {
        using var response = await PostInvoiceAsync(CarrierInvoice(carrier, invoiceNumber, lines));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await BodyOfAsync(response))["invoice"]!.AsObject();
    }

    /// <summary>The result matching found for a line of an invoice, as the ERP answered with it.</summary>
    protected static string ResultOf(JsonObject invoice, string reference) => LineOf(invoice, reference)["result"]!.GetValue<string>();

    protected static JsonObject LineOf(JsonObject invoice, string reference) =>
        invoice["lines"]!.AsArray().Select(line => line!.AsObject()).Single(line => line["carrierBookingReference"]!.GetValue<string>() == reference);

    /// <summary>The Carrier references of the Open Carrier costs Finance sees.</summary>
    protected async Task<IReadOnlyList<string>> OpenCostsAsync() =>
        [.. (await Finance.AskAsync(new ListOpenCarrierCostsQuery(), Ct)).SelectMany(carrier => carrier.Costs).Select(cost => cost.CarrierBookingReference)];

    /// <summary>A Carrier invoice as Finance sees it in the Carrier invoices list.</summary>
    protected async Task<Application.CarrierInvoices.CarrierInvoiceView> ShownAsync(string invoiceNumber) =>
        (await Finance.AskAsync(new ListCarrierInvoicesQuery(), Ct)).Select(item => item.Invoice).Single(invoice => invoice.InvoiceNumber == invoiceNumber);

    /// <summary>Reports on a Carrier invoice as the Carrier invoice outcome Logic App does.</summary>
    protected Task<HttpResponseMessage> ReportCarrierInvoiceAsync(
        string outcome, string invoiceNumber, string? correlationId, object? body = null, string carrier = Carrier, string[]? roles = null) =>
        ReportAsync("carrier-invoices", $"{carrier}/{invoiceNumber}", outcome, correlationId, body, roles ?? [ErpFixture.ReportCarrierInvoiceRole]);
}
