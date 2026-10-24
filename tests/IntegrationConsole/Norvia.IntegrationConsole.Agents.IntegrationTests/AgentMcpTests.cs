using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.IntegrationConsole.Application.Agents.Explain;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Runs.Open;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Open;

namespace Norvia.IntegrationConsole.Agents.IntegrationTests;

/// <summary>
/// The Integration operations agent's path in memory: the MCP stand-in that plays API Management's MCP server, and the console's agent API behind it.
/// Each test starts the two applications and gives them the data it reads, so none depends on another.
/// </summary>
public sealed class AgentMcpTests
{
    private const string UnmappableTrackingMessage = "UnmappableTrackingMessage";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACallWithNoToken_IsRefused401()
    {
        await using var host = new AgentHost();
        using var client = host.StandInClient(token: string.Empty);

        using var response = await CallAsync(client, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["messageId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ACallWithATokenTheStandInDoesNotTrust_IsRefused401()
    {
        await using var host = new AgentHost();
        using var client = host.StandInClient(host.ForgedAgentToken());

        using var response = await CallAsync(client, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallWithATokenWithoutTheReadRole_IsRefused403()
    {
        await using var host = new AgentHost();
        using var client = host.StandInClient(host.AgentToken(roles: ["Something.Else"]));

        using var response = await CallAsync(client, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ACallOverTheRateLimit_IsRefused429WithRetryAfter()
    {
        await using var host = new AgentHost(rateLimitCalls: 2);
        using var client = host.StandInClient();
        (await CallAsync(client, "tools/list")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CallAsync(client, "tools/list")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await CallAsync(client, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task TheChecksAreMadeInOrder_ARateLimitedAgentIsStillAskedForItsTokenAndItsRoleFirst()
    {
        await using var host = new AgentHost(rateLimitCalls: 1);
        using var agent = host.StandInClient();
        (await CallAsync(agent, "tools/list")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var noToken = host.StandInClient(token: string.Empty);
        using var forged = host.StandInClient(host.ForgedAgentToken());
        using var noRole = host.StandInClient(host.AgentToken(roles: []));

        (await CallAsync(noToken, "tools/list")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CallAsync(forged, "tools/list")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CallAsync(noRole, "tools/list")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CallAsync(agent, "tools/list")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task GetMessageTrail_ReturnsTheTrailTheConsolesPageShows_WithNoMessageBodies()
    {
        await using var host = new AgentHost();
        var trail = SeedFailedTrail(host);
        using var client = host.StandInClient();

        var text = await ToolTextAsync(client, "get_message_trail", new() { ["correlationId"] = trail.CorrelationId });

        var page = await OpenTrailAsync(host, trail.CorrelationId);
        var view = JsonNode.Parse(text)!;
        view["correlationId"]!.GetValue<string>().Should().Be(trail.CorrelationId);
        Hops(view).Should().Equal(
            page.Hops.Select(hop => ((string?)hop.Name, hop.Flow, hop.Outcome?.ToString(), hop.FailureCode, hop.Run?.RunId)),
            "the Agent reads the hops the page shows");
        view["businessKeys"]!.AsArray().Select(key => key!["value"]!.GetValue<string>()).Should().Equal(page.BusinessKeys.Select(key => key.Value));
        PropertyNames(view).Should().BeSubsetOf(AllowedTrailProperties, "the agent view names statuses, codes, times and business names only");
    }

    [Fact]
    public async Task GetRun_ReturnsTheRunTheConsolesPageShows_WithNoStepInputsOrOutputs()
    {
        await using var host = new AgentHost();
        var trail = SeedFailedTrail(host);
        using var client = host.StandInClient();
        var run = trail.Run;

        var text = await ToolTextAsync(client, "get_run", new() { ["logicApp"] = run.LogicApp, ["workflow"] = run.Workflow, ["runId"] = run.RunId });

        var page = await OpenRunAsync(host, run);
        var view = JsonNode.Parse(text)!;
        view["status"]!.GetValue<string>().Should().Be(page.Status);
        view["steps"]!.AsArray().Select(step => (step!["name"]!.GetValue<string>(), step["status"]!.GetValue<string>(), step["error"]?["code"]?.GetValue<string>()))
            .Should().Equal(page.Steps.Select(step => (step.Name, step.Status, step.Error?.Code)));
        PropertyNames(view).Should().BeSubsetOf(AllowedRunProperties);
        host.Runs.BodiesAsked.Should().BeFalse("the agent view never reads a step's bodies");
    }

    [Fact]
    public async Task ExplainingATrail_PutsTheToolCallsOnTheTrail_TaggedWithTheAgentAndTheTool()
    {
        await using var host = new AgentHost();
        var trail = SeedFailedTrail(host);

        var result = await ExplainAsync(host, trail.CorrelationId);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value.Evidence.Select(evidence => evidence.ToolName).Should().Contain(["get_message_trail", "get_run"], "the answer cites what each tool returned");
        var page = await OpenTrailAsync(host, trail.CorrelationId);
        var calls = page.Hops.Where(hop => hop.AgentId is not null).ToList();
        calls.Select(hop => hop.AgentId).Distinct().Should().Equal(AgentHost.AgentId);
        calls.Select(hop => hop.ToolName).Distinct().Should().Contain(["get_message_trail", "get_run"]);
        calls.Should().OnlyContain(hop => hop.Outcome == Outcome.Succeeded);
    }

    [Fact]
    public async Task AToolCallTheAgentViewRefuses_IsAgentToolRefused_AndTheConsoleSaysTheAssistantIsUnavailable()
    {
        await using var host = new AgentHost(consoleDistrustsStandIn: true);
        var trail = SeedFailedTrail(host);

        var result = await ExplainAsync(host, trail.CorrelationId);

        // The page shows "Assistant unavailable" for any unsuccessful result (ExplainTrail.razor), whatever the code.
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain("AgentToolRefused");
        var page = await OpenTrailAsync(host, trail.CorrelationId);
        page.Hops.Should().Contain(
            hop => hop.ToolName == "get_message_trail" && hop.Outcome == Outcome.Refused && hop.FailureCode == "AgentToolRefused" && hop.AgentId == AgentHost.AgentId,
            "the refusal is on the Message trail, tagged with the Agent and the tool");
    }

    private static readonly HashSet<string> AllowedTrailProperties =
    [
        "correlationId", "hops", "businessKeys", "name", "flow", "time", "durationMs", "outcome", "failureCode", "fault", "agentId", "toolName",
        "run", "logicApp", "workflow", "runId", "deadLetter", "kind", "value",
    ];

    private static readonly HashSet<string> AllowedRunProperties =
    [
        "logicApp", "workflow", "runId", "isKept", "status", "steps", "name", "durationMs", "error", "code", "message",
    ];

    private sealed record Seeded(string CorrelationId, WorkflowRun Run);

    /// <summary>A trail of its own: a request, then a Logic App run that failed, and the run's history.</summary>
    private static Seeded SeedFailedTrail(AgentHost host)
    {
        var correlationId = Guid.NewGuid().ToString();
        var run = new WorkflowRun("norvia-integration-logic-apps", "carrier-tracking", "08584" + Guid.NewGuid().ToString("N")[..12]);
        var now = DateTimeOffset.UtcNow;
        host.Telemetry.Seed(
            correlationId,
            new RecordedHop("seed-1", null, "POST /v1/tracking", "carrier-tracking", now.AddSeconds(-30), TimeSpan.FromMilliseconds(40), Outcome.Succeeded, null, [new BusinessKey(BusinessKeyKind.CarrierBookingNumber, "CB-" + correlationId[..8])]),
            new RecordedHop("seed-2", "seed-1", "carrier-tracking run", "carrier-tracking", now.AddSeconds(-20), TimeSpan.FromSeconds(2), Outcome.Failed, UnmappableTrackingMessage, [], Run: run));
        host.Runs.Keep(
            run,
            new RecordedRun(
                "Failed",
                now.AddSeconds(-20),
                now.AddSeconds(-18),
                [
                    new RecordedStep("Receive", "Succeeded", now.AddSeconds(-20), now.AddSeconds(-19), null, false, false),
                    new RecordedStep("Map", "Failed", now.AddSeconds(-19), now.AddSeconds(-18), new StepError(UnmappableTrackingMessage, "Status code DLV is not in the map."), true, true),
                ]));
        return new Seeded(correlationId, run);
    }

    private static async Task<MessageTrail> OpenTrailAsync(AgentHost host, string correlationId)
    {
        using var scope = host.ConsoleServices.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new OpenTrailQuery(correlationId), Ct);
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        return result.Value;
    }

    private static async Task<RunDetail> OpenRunAsync(AgentHost host, WorkflowRun run)
    {
        using var scope = host.ConsoleServices.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new OpenRunQuery(run.LogicApp, run.Workflow, run.RunId), Ct);
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        return result.Value;
    }

    private static async Task<Result<Norvia.Agents.Suggestion>> ExplainAsync(AgentHost host, string correlationId)
    {
        using var scope = host.ConsoleServices.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ExplainTrailQuery(correlationId), Ct);
    }

    // The agent view's hops as the page lists them, in the order the page shows them.
    private static List<(string?, string?, string?, string?, string?)> Hops(JsonNode view) =>
    [
        .. view["hops"]!.AsArray().Select(hop => (
            hop!["name"]?.GetValue<string>(),
            hop["flow"]?.GetValue<string>(),
            hop["outcome"]?.GetValue<string>(),
            hop["failureCode"]?.GetValue<string>(),
            hop["run"]?["runId"]?.GetValue<string>())),
    ];

    private static IEnumerable<string> PropertyNames(JsonNode? node) => node switch
    {
        JsonObject properties => properties.SelectMany(property => PropertyNames(property.Value).Prepend(property.Key)),
        JsonArray items => items.SelectMany(PropertyNames),
        _ => [],
    };

    private static Task<HttpResponseMessage> CallAsync(HttpClient client, string method, JsonObject? parameters = null) =>
        client.PostAsJsonAsync(
            new Uri("/mcp", UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters ?? new JsonObject() },
            Ct);

    /// <summary>Calls a tool and returns the text it answered with, the agent view's JSON.</summary>
    private static async Task<string> ToolTextAsync(HttpClient client, string tool, Dictionary<string, string> arguments)
    {
        var parameters = new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = new JsonObject(arguments.Select(argument => KeyValuePair.Create(argument.Key, (JsonNode?)JsonValue.Create(argument.Value)))),
        };
        using var response = await CallAsync(client, "tools/call", parameters);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reply = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!;
        reply["isError"]!.GetValue<bool>().Should().BeFalse();
        return reply["content"]![0]!["text"]!.GetValue<string>();
    }
}
