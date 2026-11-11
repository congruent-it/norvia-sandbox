using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.AppHost;
using Norvia.Tms.TestSupport.Exceptions;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>
/// The Planner notification agent's draft of a Notify Shipper message (P7), for a Shipment each test books itself. Its identity can call only
/// <c>get_shipper_notification_template</c> and <c>get_shipper_view</c> and is refused on every other Agent's tool; for an Exception with a Carrier and an internal remark neither tool shows
/// them. A message sent from a draft names the Agent and its run on its trace (ADR 0040), edited or not; one typed by hand names none.
/// </summary>
public sealed class ShipperNotificationDraftTests(PlannerAgentFixture fixture)
{
    // Written out, not read from the handler's constant, so a changed name shows.
    private const string NotificationAgentName = "planner-notification";
    private const string PlannerNotificationAgentFlow = "agent-view-planner-notification";
    private const string CarrierName = "Proef Transport";

    private static readonly string[] OwnTools = [PlannerAgentCalls.TemplateTool, PlannerAgentCalls.ShipperViewTool];

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    // Every tool of every other Agent's server (contracts/agent-mcp), none of which the notification agent's identity may call. A tool added to a contract is covered at once.
    public static TheoryData<string> ToolsOfOtherAgents()
    {
        var names = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "contracts"), "*.tools.json")
            .SelectMany(file => JsonNode.Parse(File.ReadAllText(file))!["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()))
            .Except(OwnTools)
            .Distinct()
            .Order(StringComparer.Ordinal);
        var data = new TheoryData<string>();
        data.AddRange(names);
        return data;
    }

    [Fact]
    public async Task TheNotificationAgentsServer_ListsTheTemplateAndTheShipperView_AndNothingElse()
    {
        using var client = fixture.CreateMcpClient(fixture.NotificationAgentToken());

        using var response = await client.CallAsync("tools/list", _cancellationToken, PlannerAgentFixture.NotificationMcpPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tools = (await response.Content.ReadFromJsonAsync<JsonObject>(_cancellationToken))!["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().BeEquivalentTo(OwnTools);
    }

    [Theory]
    [MemberData(nameof(ToolsOfOtherAgents))]
    public async Task AToolOfAnotherAgent_IsRefused403_OnTheNotificationAgentsServer(string tool)
    {
        using var client = fixture.CreateMcpClient(fixture.NotificationAgentToken());

        using var response = await client.CallToolRawAsync(
            tool, new JsonObject { ["transportOrderNumber"] = "TO-9999999", ["exceptionNumber"] = "EX-9999999" }, _cancellationToken, PlannerAgentFixture.NotificationMcpPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheNotificationAgentsToken_IsRefused401_OnThePlannerAgentsServer()
    {
        using var client = fixture.CreateMcpClient(fixture.NotificationAgentToken());

        using var response = await client.CallAsync("tools/list", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the token is for the notification agent's server");
    }

    [Fact]
    public async Task ForAnExceptionWithACarrierAndAnInternalRemark_NeitherToolShowsThem()
    {
        var open = await ExceptionWithACarrierAndARemarkAsync();
        using var client = fixture.CreateMcpClient(fixture.NotificationAgentToken());

        var template = await ReadAsync(client, PlannerAgentCalls.TemplateTool, new JsonObject { ["exceptionNumber"] = open.ExceptionNumber });
        var view = await ReadAsync(client, PlannerAgentCalls.ShipperViewTool, new JsonObject { ["transportOrderNumber"] = open.TransportOrderNumber });

        template.Should().Contain("delivery", "it is the template for the Exception's type").And.NotContainAny(open.Hidden);
        view.Should().Contain(open.TransportOrderNumber).And.NotContainAny(open.Hidden);
    }

    [Fact]
    public async Task TheDraft_LeaksNothingOfTheCarrierTheRemarkOrTheException()
    {
        var open = await ExceptionWithACarrierAndARemarkAsync();

        var run = await fixture.DraftShipperNotificationAsync(PlannerAgentFixture.NewAgentId(), open.ExceptionNumber, open.TransportOrderNumber, Guid.NewGuid().ToString(), _cancellationToken);

        run.Suggestion.Cause.Should().NotBeNullOrWhiteSpace().And.NotContainAny(open.Hidden);
    }

    [Fact]
    public async Task TheDraft_ReadsOnlyTheTemplateAndTheShipperView_OnTheTrailOfTheExceptionAsTheAgent()
    {
        var open = await ExceptionWithACarrierAndARemarkAsync();
        var agentId = PlannerAgentFixture.NewAgentId();
        var correlationId = Guid.NewGuid().ToString();

        var run = await fixture.DraftShipperNotificationAsync(agentId, open.ExceptionNumber, open.TransportOrderNumber, correlationId, _cancellationToken);

        run.Suggestion.Evidence.Select(evidence => evidence.ToolName).Distinct().Should().BeEquivalentTo(OwnTools);
        foreach (var tool in OwnTools)
        {
            await fixture.TmsLogAsync(
                log => log["flow"] == PlannerNotificationAgentFlow && log["correlationId"] == correlationId && log["agentId"] == agentId && log["toolName"] == tool && log["outcome"] == "Succeeded",
                _cancellationToken);
        }
    }

    // The notification agent's token is for the TMS API's notification view only: every other agent view refuses it, whoever calls (API Management, or the stand-in).
    [Theory]
    [InlineData("/v1/agent/planner/transport-orders/TO-9999999/assignment-options")]
    [InlineData("/v1/agent/planner/exceptions/EX-9999999/timeline")]
    [InlineData("/v1/agent/customer-service/transport-orders/TO-9999999")]
    [InlineData("/v1/agent/customer-service/shipments/SH-9999999/exceptions")]
    [InlineData("/v1/agent/customer-service-reply/transport-orders/TO-9999999/shipper-view")]
    public async Task TheNotificationAgentsIdentity_IsRefused403_OnTheOtherAgentViewsOfTheTmsApi(string path)
    {
        var token = fixture.IssueToken([LocalTokens.PlannerNotificationAgentReadRole], LocalTokens.TmsAudience, LocalTokens.PlannerNotificationAgentClientId);
        using var client = fixture.CreateTmsClient(token);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AMessageSentFromADraft_NamesTheAgentAndItsRunOnItsTrace_EditedOrNot(bool edited)
    {
        var open = await ExceptionWithACarrierAndARemarkAsync();
        var run = await fixture.DraftShipperNotificationAsync(PlannerAgentFixture.NewAgentId(), open.ExceptionNumber, open.TransportOrderNumber, Guid.NewGuid().ToString(), _cancellationToken);
        var notification = ShipperNotificationCalls.Notification(edited ? run.Suggestion.Cause + "\n\nPlease call us with questions." : run.Suggestion.Cause);
        notification["agentName"] = NotificationAgentName;
        notification["agentRunId"] = run.RunId;

        var sent = await NotifiedAsync(open.ExceptionNumber, notification);

        var trace = await SucceededTraceAsync(sent["correlationId"]!.GetValue<string>());
        trace["agentId"].Should().Be(NotificationAgentName);
        trace["agentRunId"].Should().Be(run.RunId);
    }

    [Fact]
    public async Task AMessageTypedByHand_NamesNoAgentOnItsTrace()
    {
        var open = await ExceptionWithACarrierAndARemarkAsync();

        var sent = await NotifiedAsync(open.ExceptionNumber, ShipperNotificationCalls.Notification());

        var trace = await SucceededTraceAsync(sent["correlationId"]!.GetValue<string>());
        trace["agentId"].Should().BeNull();
        trace["agentRunId"].Should().BeNull();
    }

    /// <summary>An open Exception of a Carrier Shipment, with an internal remark on it, and what the Shipper must never be told of it.</summary>
    private sealed record NotifiableException(string ExceptionNumber, string TransportOrderNumber, string[] Hidden);

    private async Task<NotifiableException> ExceptionWithACarrierAndARemarkAsync()
    {
        var (shipmentNumber, carrierBookingNumber, reference, exceptionNumber) = await fixture.BookedWithDeliveryFailedAsync(_cancellationToken);
        var remark = $"Internal remark {Guid.NewGuid():N}";
        using var noted = await fixture.AddNoteAsync(exceptionNumber, ExceptionCalls.Note(remark), _cancellationToken);
        noted.StatusCode.Should().Be(HttpStatusCode.OK);
        var shipment = await fixture.GetShipmentAsync(shipmentNumber, _cancellationToken);
        return new(exceptionNumber, shipment["transportOrderNumber"]!.GetValue<string>(), [PlanningCalls.CarrierId, CarrierName, carrierBookingNumber, reference, remark, exceptionNumber]);
    }

    private async Task<string> ReadAsync(HttpClient client, string tool, JsonObject arguments)
    {
        var (isError, text) = await client.CallToolAsync(tool, arguments, Guid.NewGuid().ToString(), _cancellationToken, PlannerAgentFixture.NotificationMcpPath);
        isError.Should().BeFalse(text);
        return text;
    }

    private async Task<JsonNode> NotifiedAsync(string exceptionNumber, JsonObject notification)
    {
        using var response = await fixture.NotifyShipperAsync(exceptionNumber, _cancellationToken, notification);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    private Task<TmsLog> SucceededTraceAsync(string correlationId) =>
        fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Succeeded", _cancellationToken);
}
