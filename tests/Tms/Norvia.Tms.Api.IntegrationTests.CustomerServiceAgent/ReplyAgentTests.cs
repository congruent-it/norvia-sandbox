using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.Agents.Definitions;
using Norvia.Agents.Runner;
using Norvia.Agents.Scripts;
using Norvia.AppHost;

namespace Norvia.Tms.Api.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// The Customer service reply agent (#426, ADR 0011): its identity can call only <c>get_shipper_view</c> and is refused (403) on every other agent-view tool, both on its own MCP server
/// and on the TMS API's agent view behind it. For an order with a Carrier and an internal Exception the Shipper view shows neither, and the draft is composed from that result only.
/// Each test carries its own Transport Order to the state it needs and calls with an Agent identity of its own.
/// </summary>
public sealed class ReplyAgentTests(CustomerServiceAgentFixture fixture)
{
    private const string ShipperViewTool = "get_shipper_view";

    // Where the Customer service agent's view has them, the reply agent's must not: the Carrier, the Shipment, the Exception and what reporters typed.
    private static readonly string[] NeverForTheReplyAgent =
    [
        PlanningCalls.CarrierId, "exceptionNumber", "shipmentNumber", "carrier", "Phoned Kessler", "at dock 4", "J. Becker", "Signed at the dock", "cmr.pdf",
        .. CustomerServiceAgentCalls.NeverForTheAgent,
    ];

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheReplyAgentsIdentity_ListsOnlyTheShipperView()
    {
        using var client = ReplyClient(fixture.ReplyAgentToken());

        using var response = await client.CallAsync(CustomerServiceAgentFixture.ReplyMcpPath, "tools/list", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tools = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Equal(ShipperViewTool);
    }

    [Theory]
    [InlineData(CustomerServiceAgentCalls.Tool)]
    [InlineData("get_transport_order")]
    [InlineData("get_shipment_timeline")]
    [InlineData("get_exceptions")]
    [InlineData("get_proof_of_delivery_status")]
    [InlineData("get_release_eligibility")]
    public async Task TheReplyAgentsIdentity_IsRefused403_OnEveryOtherAgentViewTool(string tool)
    {
        using var client = ReplyClient(fixture.ReplyAgentToken());

        using var response = await client.CallToolRawAsync(CustomerServiceAgentFixture.ReplyMcpPath, tool, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheReplyAgentsRole_IsRefused403_OnTheCustomerServiceAgentsView_AndTheOtherWayRound()
    {
        var number = await DeliveredOrderWithAnExceptionAsync();
        using var asReply = fixture.CreateTmsClient(fixture.AgentViewToken([LocalTokens.CustomerServiceReplyAgentReadRole]));
        using var asCustomerService = fixture.CreateTmsClient(fixture.AgentViewToken([LocalTokens.CustomerServiceAgentReadRole]));

        var replyOnCustomerService = await asReply.GetAsync(new Uri($"/v1/agent/customer-service/transport-orders/{number.TransportOrderNumber}", UriKind.Relative), _cancellationToken);
        var replyOnExceptions = await asReply.GetAsync(new Uri($"/v1/agent/customer-service/shipments/{number.ShipmentNumber}/exceptions", UriKind.Relative), _cancellationToken);
        var customerServiceOnReply = await asCustomerService.GetAsync(new Uri($"/v1/agent/customer-service-reply/transport-orders/{number.TransportOrderNumber}/shipper-view", UriKind.Relative), _cancellationToken);
        var replyOnReply = await asReply.GetAsync(new Uri($"/v1/agent/customer-service-reply/transport-orders/{number.TransportOrderNumber}/shipper-view", UriKind.Relative), _cancellationToken);

        replyOnCustomerService.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        replyOnExceptions.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        customerServiceOnReply.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        replyOnReply.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForAnOrderWithACarrierAndAnInternalException_TheShipperView_ShowsNeither()
    {
        var order = await DeliveredOrderWithAnExceptionAsync();
        var open = (await fixture.OpenExceptionsOfAsync(order.ShipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;
        using var client = ReplyClient(fixture.ReplyAgentToken());

        var (isError, text) = await client.CallReplyToolAsync(ShipperViewTool, order.TransportOrderNumber, Guid.NewGuid().ToString(), _cancellationToken);

        isError.Should().BeFalse(text);
        var view = JsonNode.Parse(text)!;
        view["transportOrderNumber"]!.GetValue<string>().Should().Be(order.TransportOrderNumber);
        view["trackingStatus"]!.GetValue<string>().Should().Be("Delivered");
        view["proofOfDelivery"]!["available"]!.GetValue<bool>().Should().BeTrue();
        text.Should().NotContainAny(NeverForTheReplyAgent).And.NotContain(order.ShipmentNumber).And.NotContain(order.CarrierBookingReference)
            .And.NotContain(open["exceptionNumber"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(ShipperQuestion.WhereIsMyOrder)]
    [InlineData(ShipperQuestion.HasItBeenDelivered)]
    [InlineData(ShipperQuestion.IsThereProofOfDelivery)]
    public async Task TheDraft_IsComposedFromTheShipperViewOnly(ShipperQuestion question)
    {
        var order = await DeliveredOrderWithAnExceptionAsync();
        var open = (await fixture.OpenExceptionsOfAsync(order.ShipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;
        var agentId = CustomerServiceAgentFixture.NewAgentId();

        var run = await DraftAsync(agentId, order.TransportOrderNumber, question);

        run.ToolCalls.Select(call => call.ToolName).Should().Equal(ShipperViewTool);
        run.Suggestion.Evidence.Should().NotBeEmpty().And.OnlyContain(evidence => evidence.ToolName == ShipperViewTool);
        run.Suggestion.Cause.Should().StartWith("Hello,").And.Contain(order.TransportOrderNumber);
        var everything = run.Suggestion.Cause + string.Join(' ', run.Suggestion.Evidence.Select(evidence => evidence.Claim)) + run.Suggestion.SuggestedAction;
        everything.Should().NotContainAny(NeverForTheReplyAgent).And.NotContain(order.ShipmentNumber).And.NotContain(order.CarrierBookingReference)
            .And.NotContain(open["exceptionNumber"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheShipperViewCall_IsOnTheStandInsAndTheAgentViewsTrail_TaggedWithTheReplyAgentAndItsTool()
    {
        var order = await DeliveredOrderWithAnExceptionAsync();
        var agentId = CustomerServiceAgentFixture.NewAgentId();
        var correlationId = Guid.NewGuid().ToString();

        await DraftAsync(agentId, order.TransportOrderNumber, ShipperQuestion.WhereIsMyOrder, correlationId);

        await fixture.StandInLogAsync(log => IsHop(log, "agent-mcp-customer-service-reply", correlationId, agentId), _cancellationToken);
        await fixture.TmsLogAsync(log => IsHop(log, "agent-view-customer-service-reply", correlationId, agentId), _cancellationToken);
    }

    private static bool IsHop(TmsLog log, string flow, string correlationId, string agentId) =>
        log["flow"] == flow && log["correlationId"] == correlationId && log["agentId"] == agentId && log["toolName"] == ShipperViewTool && log["outcome"] == "Succeeded";

    private HttpClient ReplyClient(string token) => fixture.CreateMcpClient(token);

    /// <summary>A Transport Order Booked with the test Carrier, Delivered, with a Proof of delivery that records reservations, which opens an internal Exception.</summary>
    private async Task<BillingCalls.BookedOrder> DeliveredOrderWithAnExceptionAsync()
    {
        var number = await fixture.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await fixture.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        var booked = await fixture.BookedAsync(number, _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        await fixture.DeliveredAsync(booked, correlationId, _cancellationToken);
        await fixture.PublishedAsync(correlationId, "ShipmentDelivered", _cancellationToken);
        await fixture.ProofWithReservationsRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        var open = (await fixture.OpenExceptionsOfAsync(booked.ShipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;
        using var added = await fixture.AddNoteAsync(open["exceptionNumber"]!.GetValue<string>(), ExceptionCalls.Note(), _cancellationToken);
        added.IsSuccessStatusCode.Should().BeTrue();
        return booked;
    }

    /// <summary>The reply agent's run, as the TMS UI runs it in process: its definition, the scripted model and the stand-in's tools, with the reply agent's token.</summary>
    private async Task<AgentRunResult> DraftAsync(string agentId, string transportOrderNumber, ShipperQuestion question, string? correlationId = null)
    {
        var definition = AgentDefinitionLoader.Parse(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "agents", "customer-service-reply.agent.yaml"), _cancellationToken));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.ReplyAgentToken(agentId));
        var runner = new AgentRunner(CustomerServiceReplyScript.Model(), new McpToolSource(http, fixture.ReplyMcpAddress), new AgentRunnerOptions(TimeSpan.FromSeconds(30)));
        return await runner.RunAsync(
            definition,
            new AgentQuestion(CustomerServiceQuestions.DraftReply(transportOrderNumber, question), correlationId ?? Guid.NewGuid().ToString(), Guid.NewGuid().ToString("N")),
            _cancellationToken);
    }
}

/// <summary>What the reply tests do against the reply agent's MCP server.</summary>
internal static class ReplyAgentCalls
{
    public static Task<HttpResponseMessage> CallAsync(this HttpClient client, string path, string method, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = new JsonObject() },
            cancellationToken);

    /// <summary>Calls a tool by name with a Transport Order number, whatever the tool's own argument is, and returns the raw response.</summary>
    public static Task<HttpResponseMessage> CallToolRawAsync(this HttpClient client, string path, string tool, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = new JsonObject { ["transportOrderNumber"] = "TO-0000001", ["shipmentNumber"] = "SH-0000001", ["id"] = Guid.NewGuid().ToString("D") } },
            },
            cancellationToken);

    public static async Task<(bool IsError, string Text)> CallReplyToolAsync(
        this HttpClient client, string tool, string transportOrderNumber, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(CustomerServiceAgentFixture.ReplyMcpPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = new JsonObject { ["transportOrderNumber"] = transportOrderNumber } },
            }),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["result"]!;
        return (result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
    }
}
