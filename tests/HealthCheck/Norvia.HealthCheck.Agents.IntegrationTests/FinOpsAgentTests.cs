using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.HealthCheck.Application.Narrate;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The FinOps agent's path in memory (#511): its own server on the MCP stand-in (<c>/mcp/health-check-cost</c>, which plays API Management's MCP
/// server) and the cost reads of the app's agent API behind it, each behind a role of its own. The rising spend is the one the <c>incident-2</c>
/// snapshot and the modelled <c>chatty-telemetry</c> bill hold: the Log Analytics workspace's spend rises with the Transport Orders flat.
/// Each test starts the two applications of its own.
/// </summary>
public sealed class FinOpsAgentTests
{
    private const string CostToolsPath = "/mcp/health-check-cost";
    private const string HealthCheckToolsPath = "/mcp/health-check";
    private const string CostApiPath = "/agent/v1/cost";
    private const string RisingSpendApiPath = "/agent/v1/rising-spend";
    private const string StandInFlow = "agent-mcp-health-check-cost";
    private const string AgentViewFlow = "agent-view-health-check";
    private const string RisingResource = "log-workspace";
    private const string RisingFinding = "F-COST-RISING-SPEND";
    private const string CostView = "get_cost_view";
    private const string RisingSpend = "get_rising_spend_findings";

    /// <summary>The estate whose Log Analytics workspace's spend rises with the Transport Orders flat, and the modelled bill that shows it.</summary>
    private static HealthCheckAgentHost ChattyEstate(bool appDistrustsStandIn = false) => new("incident-2", appDistrustsStandIn: appDistrustsStandIn, costSnapshot: "chatty-telemetry");

    /// <summary>The fixed estate and its flat bill: nothing rises.</summary>
    private static HealthCheckAgentHost FlatEstate() => new("fixed", costSnapshot: "fixed");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACallWithNoToken_IsRefused401()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.CostStandInClient(token: string.Empty);

        using var response = await CallAsync(client, CostToolsPath, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["messageId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ACallWithATokenTheStandInDoesNotTrust_IsRefused401()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.CostStandInClient(host.ForgedAgentToken(HealthCheckAgentHost.CostAgentAudience));

        using var response = await CallAsync(client, CostToolsPath, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallWithATokenWithoutTheCostRole_IsRefused403()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.CostStandInClient(host.CostToken(roles: ["Something.Else"]));

        using var response = await CallAsync(client, CostToolsPath, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheHealthCheckAgentsToken_OpensNothingOnTheFinOpsServer()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.CostStandInClient(host.AgentToken());

        using var response = await CallAsync(client, CostToolsPath, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "it is for another audience");
    }

    [Fact]
    public async Task TheFinOpsAgentsToken_OpensNothingOnTheHealthCheckServer()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.StandInClient(host.CostToken());

        using var response = await CallAsync(client, HealthCheckToolsPath, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "it is for another audience");
    }

    [Fact]
    public async Task ACallOverTheRateLimit_IsRefused429WithRetryAfter()
    {
        await using var host = new HealthCheckAgentHost(rateLimitCalls: 2);
        using var client = host.CostStandInClient();
        (await CallAsync(client, CostToolsPath, "tools/list")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CallAsync(client, CostToolsPath, "tools/list")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await CallAsync(client, CostToolsPath, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task ToolsList_NamesTheTwoCostTools_AndNoOther()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.CostStandInClient();

        using var response = await CallAsync(client, CostToolsPath, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tools = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!["tools"]!.AsArray();
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().BeEquivalentTo(CostView, RisingSpend);
    }

    [Fact]
    public async Task ACostToolCalledOnTheHealthCheckServer_IsRefused403()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.StandInClient();

        using var response = await CallAsync(client, HealthCheckToolsPath, "tools/call", new JsonObject { ["name"] = CostView, ["arguments"] = new JsonObject() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the health-check agent has no cost tool");
    }

    [Fact]
    public async Task TheAppsCostReads_RefuseNoTokenAndTheHealthCheckRole_AndAnswerTheFinOpsRole()
    {
        await using var host = new HealthCheckAgentHost();
        using var anonymous = host.AppClient(token: string.Empty);
        using var healthCheck = host.AppClient(host.AgentToken(audience: HealthCheckAgentHost.ApiAudience));
        using var finOps = host.AppClient(host.CostToken(audience: HealthCheckAgentHost.ApiAudience));

        foreach (var path in new[] { CostApiPath, RisingSpendApiPath })
        {
            (await anonymous.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
            (await healthCheck.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            (await finOps.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task TheAppsHealthCheckRead_RefusesTheFinOpsRole()
    {
        await using var host = new HealthCheckAgentHost();
        using var finOps = host.AppClient(host.CostToken(audience: HealthCheckAgentHost.ApiAudience));

        (await finOps.GetAsync("/agent/v1/checks", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCostView_OnTheChattyTelemetryBill_ShowsTheWorkspaceRisingAndTheVolumeFlat_EachChangeWorkedOutByCode()
    {
        await using var host = ChattyEstate();
        using var client = host.CostStandInClient();

        var view = JsonNode.Parse(await ToolTextAsync(client, CostView))!;

        view["periodDays"]!.GetValue<int>().Should().Be(7);
        view["total"]!["change"]!.GetValue<string>().Should().StartWith("+");
        var volume = view["transportOrders"]!;
        volume["change"]!.GetValue<string>().Should().MatchRegex(@"^[+-]?\d+(\.\d)?%$", "the change is text, copied by the agent and never worked out");
        volume["currentEurPerDay"]!.GetValue<decimal>().Should().BeApproximately(volume["previousEurPerDay"]!.GetValue<decimal>(), volume["previousEurPerDay"]!.GetValue<decimal>() * 0.1m, "the Transport Orders stay flat while the spend rises");
        var biggest = view["resources"]!.AsArray()[0]!;
        biggest["name"]!.GetValue<string>().Should().Be(RisingResource, "the biggest riser comes first");
        biggest["currentEurPerDay"]!.GetValue<decimal>().Should().BeGreaterThan(biggest["previousEurPerDay"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task GetRisingSpendFindings_OnTheChattyTelemetryBill_ReturnsTheRisingSpendFindingAlone_NamingTheWorkspaceAndTheToggle()
    {
        await using var host = ChattyEstate();
        using var client = host.CostStandInClient();

        var findings = JsonNode.Parse(await ToolTextAsync(client, RisingSpend))!;

        var finding = findings["findings"]!.AsArray().Should().ContainSingle("the one Check of the cost tool, and only its Findings").Subject!;
        finding["id"]!.GetValue<string>().Should().Be(RisingFinding);
        finding["checkId"]!.GetValue<string>().Should().Be("COST-RISING-SPEND");
        finding["asFoundToggle"]!.GetValue<string>().Should().Be("chattyTelemetry");
        finding["evidence"]!.AsArray().Select(item => item!["subject"]!.GetValue<string>()).Should().Equal(RisingResource);
    }

    [Fact]
    public async Task GetRisingSpendFindings_OnAFlatBill_ReturnsNoFinding()
    {
        await using var host = FlatEstate();
        using var client = host.CostStandInClient();

        var view = JsonNode.Parse(await ToolTextAsync(client, RisingSpend))!;

        view["findings"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task NarratingTheSpend_OnTheChattyTelemetryBill_CitesTheFindingAndPutsBothToolCallsOnTheTrail_TaggedWithTheAgentAndTheTool()
    {
        await using var host = ChattyEstate();
        var correlationId = Guid.NewGuid().ToString();

        var result = await NarrateAsync(host, correlationId);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        var item = result.Value.Items.Should().ContainSingle().Subject;
        (item.Id, item.ResultId, item.ToolName).Should().Be((RisingFinding, "r2", RisingSpend), "the Finding cites the second tool result, the rising-spend Findings");
        item.Impact.Should().Contain(RisingResource).And.Contain("chattyTelemetry");
        result.Value.Summary.Should().NotBeNullOrWhiteSpace();
        var calls = host.Telemetry.Trail(correlationId).Where(hop => hop.Flow is StandInFlow or AgentViewFlow).ToList();
        calls.Select(hop => hop.ToolName).Distinct().Should().Equal([CostView, RisingSpend], "the cost view is read first, then the Findings");
        calls.Select(hop => (hop.Flow, hop.ToolName)).Should().BeEquivalentTo(
        [
            (StandInFlow, CostView), (AgentViewFlow, CostView),
            (StandInFlow, RisingSpend), (AgentViewFlow, RisingSpend),
        ]);
        calls.Should().OnlyContain(hop => hop.AgentId == HealthCheckAgentHost.CostAgentId && hop.Outcome == "Succeeded");
    }

    [Fact]
    public async Task NarratingTheSpend_OnAFlatBill_SaysNothingRisesAndExplainsNothing()
    {
        await using var host = FlatEstate();

        var result = await NarrateAsync(host, Guid.NewGuid().ToString());

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value.Items.Should().BeEmpty();
        result.Value.Summary.Should().Contain("no rise");
    }

    [Fact]
    public async Task AToolCallTheAgentViewRefuses_IsAgentToolRefused_AndTheAppSaysTheAssistantIsUnavailable()
    {
        await using var host = ChattyEstate(appDistrustsStandIn: true);
        var correlationId = Guid.NewGuid().ToString();

        var result = await NarrateAsync(host, correlationId);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain("AgentToolRefused");
        host.Telemetry.Trail(correlationId).Should().Contain(
            hop => hop.Flow == StandInFlow && hop.ToolName == CostView && hop.Outcome == "Refused" && hop.FailureCode == "AgentToolRefused" && hop.AgentId == HealthCheckAgentHost.CostAgentId,
            "the refusal is on the Message trail, tagged with the Agent and the tool");
    }

    private static async Task<Result<Norvia.Agents.Narration>> NarrateAsync(HealthCheckAgentHost host, string correlationId)
    {
        using var scope = host.AppServices.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new NarrateSpendQuery(correlationId), Ct);
    }

    private static Task<HttpResponseMessage> CallAsync(HttpClient client, string path, string method, JsonObject? parameters = null) =>
        client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters ?? new JsonObject() },
            Ct);

    /// <summary>Calls a tool of the FinOps agent's server and returns the text it answered with, the agent view's JSON.</summary>
    private static async Task<string> ToolTextAsync(HttpClient client, string tool)
    {
        using var response = await CallAsync(client, CostToolsPath, "tools/call", new JsonObject { ["name"] = tool, ["arguments"] = new JsonObject() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reply = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!;
        reply["isError"]!.GetValue<bool>().Should().BeFalse();
        return reply["content"]![0]!["text"]!.GetValue<string>();
    }
}
