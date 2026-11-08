using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>What the tests do as the Carrier invoice rejection reason agent and its MCP server.</summary>
internal static class RejectionReasonCalls
{
    public const string Tool = "get_carrier_visible_line_facts";

    /// <summary>Every property the Carrier-visible facts may carry (<c>CarrierVisibleLineFactsView</c>); the answer's JSON names no other.</summary>
    public static readonly HashSet<string> AllowedProperties =
    [
        "carrierId", "invoiceNumber", "carrierBookingReference", "result", "invoicedAmount", "currency", "agreedCarrierCost", "likelyCorrectReference", "earlierInvoiceNumber",
    ];

    /// <summary>A <c>tools/call</c> of any tool on the server at <paramref name="path"/>: the HTTP status, and the MCP result when the call got that far.</summary>
    public static async Task<(HttpStatusCode Status, bool IsError, string Text)> CallToolAtAsync(
        this HttpClient client, string path, string tool, JsonObject arguments, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = arguments },
            }),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, true, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        var result = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["result"]!;
        return (response.StatusCode, result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
    }

    /// <summary>Calls <c>get_carrier_visible_line_facts</c> as the rejection reason agent and returns the text the tool answered with.</summary>
    public static async Task<string> ReadFactsTextAsync(
        FinanceAgentFixture fixture, string carrierId, string invoiceNumber, string reference, string correlationId, CancellationToken cancellationToken)
    {
        using var client = fixture.CreateMcpClient(fixture.RejectionReasonToken());
        var (status, isError, text) = await client.CallToolAtAsync(
            FinanceAgentFixture.RejectionReasonMcpPath,
            Tool,
            new JsonObject { ["carrierId"] = carrierId, ["invoiceNumber"] = invoiceNumber, ["carrierBookingReference"] = reference },
            correlationId,
            cancellationToken);
        status.Should().Be(HttpStatusCode.OK, text);
        isError.Should().BeFalse(text);
        return text;
    }

    /// <summary>The Carrier-visible facts of a line, parsed.</summary>
    public static async Task<JsonObject> ReadFactsAsync(
        FinanceAgentFixture fixture, string carrierId, string invoiceNumber, string reference, string correlationId, CancellationToken cancellationToken) =>
        JsonNode.Parse(await ReadFactsTextAsync(fixture, carrierId, invoiceNumber, reference, correlationId, cancellationToken))!.AsObject();

    /// <summary>The rejection reason agent's run, as the ERP UI runs it in process: its definition, the scripted model and the stand-in's one tool, with the Agent's token.</summary>
    public static async Task<AgentRunResult> DraftAsync(
        FinanceAgentFixture fixture, string agentId, string carrierId, string invoiceNumber, string reference, string correlationId, CancellationToken cancellationToken)
    {
        var definition = AgentDefinitionLoader.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "agents", "finance-rejection-reason.agent.yaml"), cancellationToken));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.RejectionReasonToken(agentId));
        var runner = new AgentRunner(FinanceRejectionReasonScript.Model(), new McpToolSource(http, fixture.RejectionReasonMcpAddress), new AgentRunnerOptions(TimeSpan.FromSeconds(30)));
        return await runner.RunAsync(
            definition,
            new AgentQuestion(FinanceQuestions.DraftRejectionReason(carrierId, invoiceNumber, reference), correlationId, Guid.NewGuid().ToString("N")),
            cancellationToken);
    }
}
