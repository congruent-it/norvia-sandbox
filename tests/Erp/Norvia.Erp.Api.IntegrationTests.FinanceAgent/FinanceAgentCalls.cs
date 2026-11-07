using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>What the tests do as the Carrier cost and Carrier invoice Logic Apps, the Agent and its MCP server.</summary>
internal static class FinanceAgentCalls
{
    public const string Tool = "get_invoice_line";
    public const string SummaryTool = "get_invoice_summary";
    public const string Carrier = "CAR-0001";
    public const string OtherCarrier = "CAR-0002";

    /// <summary>The flow the ERP API's agent view traces its calls under, and the stand-in's.</summary>
    public const string AgentViewFlow = "agent-view-finance-matching";
    public const string StandInFlow = "agent-mcp-finance-matching";

    /// <summary>The Planner's remark a Spot Carrier cost carries, which the Agent may never read. It names a person, as free text can.</summary>
    public const string SpotRemark = "Spot price agreed by phone with Sanne de Vries, +31 30 555 0142, sanne@cafedezwaan.example.";

    /// <summary>The personal data in <see cref="SpotRemark"/>, none of which may reach the Agent.</summary>
    public static readonly string[] NeverForTheAgent = ["Sanne", "de Vries", "+31 30 555 0142", "cafedezwaan"];

    private static int _bookings = Random.Shared.Next(1_000_000, 8_000_000);

    /// <summary>A Carrier booking number no other test uses.</summary>
    public static string NewBookingNumber() => $"CB-{Interlocked.Increment(ref _bookings):D7}";

    /// <summary>A Carrier booking reference no other test uses; far from any other in the ERP's ranking of candidates.</summary>
    public static string NewReference() => $"BK-{Guid.NewGuid():N}"[..15].ToUpperInvariant();

    /// <summary>A Carrier invoice number no other test uses.</summary>
    public static string NewInvoiceNumber() => $"VDT-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    /// <summary>The Canonical Model's <c>CarrierCostIncurred</c> for a Carrier booking; a Spot price, or a Carrier Rate Card version when <paramref name="rateCard"/>.</summary>
    public static JsonObject CarrierCost(string bookingNumber, string reference, decimal total, string carrier, bool rateCard)
    {
        var number = int.Parse(bookingNumber[3..], CultureInfo.InvariantCulture);
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return new JsonObject
        {
            ["eventId"] = Guid.NewGuid().ToString(),
            ["eventType"] = "CarrierCostIncurred",
            ["eventVersion"] = 1,
            ["occurredAt"] = now,
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
                    : new JsonObject { ["kind"] = "Spot", ["remark"] = SpotRemark },
                ["calculatedAt"] = now,
            },
            ["deliveryTime"] = new DateTimeOffset(2026, 10, 16, 10, 45, 0, TimeSpan.FromHours(2)).ToString("O", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Records a Carrier cost as the Carrier cost Logic App does, and returns its Carrier booking number and reference.</summary>
    public static async Task<(string BookingNumber, string Reference)> RecordCostAsync(
        FinanceAgentFixture fixture, decimal total, CancellationToken cancellationToken, string? reference = null, string carrier = Carrier, bool rateCard = false)
    {
        var bookingNumber = NewBookingNumber();
        reference ??= NewReference();
        using var client = fixture.ErpAs(FinanceAgentFixture.RecordCarrierCostRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/carrier-costs", UriKind.Relative), CarrierCost(bookingNumber, reference, total, carrier, rateCard), cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
        return (bookingNumber, reference);
    }

    /// <summary>Receives a Carrier invoice as the Carrier invoice Logic App does; returns the invoice as the ERP answered with it.</summary>
    public static async Task<JsonObject> ReceiveAsync(
        FinanceAgentFixture fixture, string invoiceNumber, CancellationToken cancellationToken, string carrier = Carrier, params (string Reference, decimal Amount)[] lines)
    {
        var invoice = new JsonObject
        {
            ["carrierId"] = carrier,
            ["invoiceNumber"] = invoiceNumber,
            ["invoiceDate"] = "2026-10-19",
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["lines"] = new JsonArray([.. lines.Select(line => (JsonNode)new JsonObject { ["carrierBookingReference"] = line.Reference, ["amount"] = line.Amount })]),
        };
        using var client = fixture.ErpAs(FinanceAgentFixture.ReceiveCarrierInvoiceRole);
        using var response = await client.PostAsJsonAsync(new Uri("/v1/carrier-invoices", UriKind.Relative), invoice, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["invoice"]!.AsObject();
    }

    /// <summary>The line of an invoice as the ERP answered with it when it received it and matched its lines (E2).</summary>
    public static JsonObject LineOf(JsonObject invoice, string reference) =>
        invoice["lines"]!.AsArray().Select(line => line!.AsObject()).Single(line => line["carrierBookingReference"]!.GetValue<string>() == reference);

    /// <summary>A JSON-RPC call of the MCP protocol.</summary>
    public static Task<HttpResponseMessage> CallAsync(this HttpClient client, string method, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            new Uri(FinanceAgentFixture.McpPath, UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = new JsonObject() },
            cancellationToken);

    /// <summary>A <c>tools/call</c> of <c>get_invoice_line</c>, as the response the stand-in answers with, for the checks that refuse it.</summary>
    public static Task<HttpResponseMessage> CallToolRawAsync(this HttpClient client, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            new Uri(FinanceAgentFixture.McpPath, UriKind.Relative),
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = Tool,
                    ["arguments"] = new JsonObject { ["carrierId"] = Carrier, ["invoiceNumber"] = NewInvoiceNumber(), ["carrierBookingReference"] = NewReference() },
                },
            },
            cancellationToken);

    /// <summary>Calls <c>get_invoice_line</c> and returns the MCP result: its <c>isError</c> and the text the tool answered with.</summary>
    public static async Task<(bool IsError, string Text)> GetInvoiceLineAsync(
        this HttpClient client, string carrierId, string invoiceNumber, string reference, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(FinanceAgentFixture.McpPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = Tool,
                    ["arguments"] = new JsonObject { ["carrierId"] = carrierId, ["invoiceNumber"] = invoiceNumber, ["carrierBookingReference"] = reference },
                },
            }),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["result"]!;
        return (result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
    }

    /// <summary>Calls <c>get_invoice_summary</c> and returns the MCP result: its <c>isError</c> and the text the tool answered with.</summary>
    public static async Task<(bool IsError, string Text)> GetInvoiceSummaryAsync(
        this HttpClient client, string carrierId, string invoiceNumber, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(FinanceAgentFixture.McpPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = SummaryTool,
                    ["arguments"] = new JsonObject { ["carrierId"] = carrierId, ["invoiceNumber"] = invoiceNumber },
                },
            }),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["result"]!;
        return (result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
    }

    /// <summary>The Agent's run, as the ERP UI runs it in process: its definition, the scripted model and the stand-in's tool, with the Agent's token.</summary>
    public static async Task<AgentRunResult> ExplainAsync(
        FinanceAgentFixture fixture, string agentId, string carrierId, string invoiceNumber, string reference, string correlationId, CancellationToken cancellationToken)
    {
        var definition = AgentDefinitionLoader.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "agents", "finance-matching.agent.yaml"), cancellationToken));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AgentToken(agentId));
        var runner = new AgentRunner(FinanceMatchingScript.Model(), new McpToolSource(http, fixture.McpAddress), new AgentRunnerOptions(TimeSpan.FromSeconds(30)));
        return await runner.RunAsync(
            definition,
            new AgentQuestion(FinanceQuestions.ExplainInvoiceLine(carrierId, invoiceNumber, reference), correlationId, Guid.NewGuid().ToString("N")),
            cancellationToken);
    }
}
