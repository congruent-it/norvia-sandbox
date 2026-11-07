using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Norvia.Erp.TestSupport;

/// <summary>What the tests do as the Carrier cost and Carrier invoice Logic Apps, on the ERP API, under numbers no other test uses.</summary>
public static class CarrierInvoiceCalls
{
    public const string RecordCarrierCostRole = "CarrierCosts.Record";
    public const string ReceiveCarrierInvoiceRole = "CarrierInvoices.Receive";
    public const string Carrier = "CAR-0001";
    public const string CarrierName = "Van Dijk Transport";

    /// <summary>A Carrier booking reference no other test uses; far from any other in the ERP's ranking of candidates.</summary>
    public static string NewReference() => $"BK-{Guid.NewGuid():N}"[..15].ToUpperInvariant();

    /// <summary>A Carrier invoice number no other test uses.</summary>
    public static string NewInvoiceNumber() => $"VDT-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    /// <summary>A client of the ERP API that sends the token a Logic App holding <paramref name="role"/> would.</summary>
    public static HttpClient Authorize(HttpClient client, string signingKey, string role)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ErpTokens.Create(signingKey, role, ErpTokens.TestLogicApp));
        return client;
    }

    /// <summary>Records a Spot Carrier cost as the Carrier cost Logic App does, and returns its Carrier booking number. With <paramref name="rateCardId"/> the cost is priced from that Carrier Rate Card instead.</summary>
    public static async Task<string> RecordCarrierCostAsync(HttpClient client, string reference, decimal total, CancellationToken cancellationToken, string? rateCardId = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        var number = Random.Shared.Next(1_000_000, 10_000_000);
        var bookingNumber = string.Create(CultureInfo.InvariantCulture, $"CB-{number:D7}");
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var cost = new JsonObject
        {
            ["eventId"] = Guid.NewGuid().ToString(),
            ["eventType"] = "CarrierCostIncurred",
            ["eventVersion"] = 1,
            ["occurredAt"] = now,
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["carrierBookingNumber"] = bookingNumber,
            ["carrierBookingReference"] = reference,
            ["shipmentNumber"] = string.Create(CultureInfo.InvariantCulture, $"SH-{number:D7}"),
            ["transportOrderNumber"] = string.Create(CultureInfo.InvariantCulture, $"TO-{number:D7}"),
            ["carrierId"] = Carrier,
            ["carrierCost"] = new JsonObject
            {
                ["currency"] = "EUR",
                ["total"] = total,
                ["chargeLines"] = new JsonArray(new JsonObject { ["type"] = "Freight", ["amount"] = total }),
                ["source"] = rateCardId is null
                    ? new JsonObject { ["kind"] = "Spot", ["remark"] = "Agreed by phone." }
                    : new JsonObject { ["kind"] = "CarrierRateCard", ["rateCardVersion"] = new JsonObject { ["rateCardId"] = rateCardId, ["version"] = 1 } },
                ["calculatedAt"] = now,
            },
            ["deliveryTime"] = new DateTimeOffset(2026, 10, 16, 10, 45, 0, TimeSpan.FromHours(2)).ToString("O", CultureInfo.InvariantCulture),
        };

        using var response = await client.PostAsJsonAsync(new Uri("/v1/carrier-costs", UriKind.Relative), cost, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
        return bookingNumber;
    }

    /// <summary>Receives a Carrier invoice as the Carrier invoice Logic App does; the ERP matches its lines (E2) and those that do not match are To decide.</summary>
    public static async Task ReceiveCarrierInvoiceAsync(
        HttpClient client, string invoiceNumber, CancellationToken cancellationToken, params (string Reference, decimal Amount)[] lines)
    {
        ArgumentNullException.ThrowIfNull(client);
        var invoice = new JsonObject
        {
            ["carrierId"] = Carrier,
            ["invoiceNumber"] = invoiceNumber,
            ["invoiceDate"] = "2026-10-19",
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["lines"] = new JsonArray([.. lines.Select(line => (JsonNode)new JsonObject { ["carrierBookingReference"] = line.Reference, ["amount"] = line.Amount })]),
        };

        using var response = await client.PostAsJsonAsync(new Uri("/v1/carrier-invoices", UriKind.Relative), invoice, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
