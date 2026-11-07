using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Tools;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>
/// The stand-in's sixth MCP server, the Carrier invoice rejection reason agent's, at <c>/mcp/finance-rejection-reason</c> (#473, ADR 0041): one
/// tool, the Carrier-visible facts of a line, behind an audience and a role of its own, so the Carrier invoice matching agent's token opens
/// nothing here and the other way round. The boundary of a text that leaves Norvia is the tool.
/// </summary>
public sealed class FinanceRejectionReasonServerTests : IAsyncLifetime
{
    private const string Facts = """{"carrierId":"CAR-0001","invoiceNumber":"VDT-1","carrierBookingReference":"TRX-1","result":"UnknownBooking"}""";

    private readonly LocalAgentMcpFactory _factory = new();

    public ValueTask InitializeAsync()
    {
        _factory.Console.Answer(HttpStatusCode.OK, Facts);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static StringContent Rpc(string method, JsonObject? parameters = null)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        return new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, HttpContent content, string path = "/mcp/finance-rejection-reason") =>
        client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

    private static JsonObject Call(string tool) =>
        new() { ["name"] = tool, ["arguments"] = new JsonObject { ["carrierId"] = "CAR-0001", ["invoiceNumber"] = "VDT-1", ["carrierBookingReference"] = "TRX-1" } };

    [Fact]
    public async Task ToolsList_IsExactlyTheCarrierVisibleFacts()
    {
        using var client = _factory.AgentClient(_factory.FinanceRejectionReasonToken());

        using var response = await Post(client, Rpc("tools/list"));

        var tools = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal("get_carrier_visible_line_facts");
    }

    [Fact]
    public async Task ToolCall_CallsTheRejectionReasonViewAsItsOwnIdentity_WithItsOwnRole()
    {
        using var client = _factory.AgentClient(_factory.FinanceRejectionReasonToken());
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "invoice-1");

        using var response = await Post(client, Rpc("tools/call", Call("get_carrier_visible_line_facts")));

        JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["result"]!["content"]![0]!["text"]!.GetValue<string>().Should().Be(Facts);
        var call = _factory.Console.Calls.Should().ContainSingle().Subject;
        call.RequestUri!.ToString().Should().Be("http://erp-api/v1/agent/finance-rejection-reason/carriers/CAR-0001/invoices/VDT-1/lines/TRX-1/carrier-visible-facts");
        call.Headers.GetValues("X-Agent-ID").Should().Equal(LocalAgentMcpFactory.FinanceRejectionReasonAgentId);
        call.Headers.GetValues("X-Tool-Name").Should().Equal("get_carrier_visible_line_facts");
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(call.Headers.Authorization!.Parameter);
        token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Should().Equal(LocalAgentMcpFactory.FinanceRejectionReasonRole);
    }

    [Fact]
    public async Task TheCarrierInvoiceMatchingAgentsToken_OpensNothingHere()
    {
        using var client = _factory.AgentClient(_factory.AgentToken(LocalAgentMcpFactory.FinanceAgentId, [LocalAgentMcpFactory.FinanceRole], LocalAgentMcpFactory.FinanceAudience));

        using var response = await Post(client, Rpc("tools/call", Call("get_carrier_visible_line_facts")));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "its audience is the Carrier invoice matching agent's view");
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRejectionReasonAgentsToken_OpensNothingOnTheFinanceMatchingServer()
    {
        using var client = _factory.AgentClient(_factory.FinanceRejectionReasonToken());

        using var response = await Post(client, Rpc("tools/list"), "/mcp/finance-matching");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenForTheRejectionReasonViewWithoutTheRole_IsRefused403()
    {
        using var client = _factory.AgentClient(_factory.FinanceRejectionReasonToken(roles: [LocalAgentMcpFactory.FinanceRole]));

        using var response = await Post(client, Rpc("tools/call", Call("get_carrier_visible_line_facts")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("get_invoice_line")]
    [InlineData("get_invoice_summary")]
    public async Task AToolOfTheCarrierInvoiceMatchingAgent_IsRefusedOnTheRejectionReasonServer(string tool)
    {
        using var client = _factory.AgentClient(_factory.FinanceRejectionReasonToken());

        using var response = await Post(client, Rpc("tools/call", Call(tool)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Console.Calls.Should().BeEmpty();
    }

    [Fact]
    public void TheAgentDefinition_ListsOnlyTheOneReadTool_TheStandInServes()
    {
        var definition = AgentDefinitionLoader.Parse(Embedded("finance-rejection-reason.agent.yaml"));
        var contract = ToolListContract.Parse(Embedded("finance-rejection-reason.tools.json"));

        contract.Unknown(definition).Should().BeEmpty();
        contract.Tools.Should().ContainSingle().Which.IsRead.Should().BeTrue();
        contract.Role.Should().Be(LocalAgentMcpFactory.FinanceRejectionReasonRole);
    }

    private static string Embedded(string name)
    {
        using var stream = typeof(FinanceRejectionReasonServerTests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
