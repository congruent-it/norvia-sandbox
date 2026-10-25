using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Estate;
using Norvia.HealthCheck.Application.Narrate;

namespace Norvia.HealthCheck.Agents.IntegrationTests;

/// <summary>
/// The Estate health-check agent's path in memory: the MCP stand-in that plays API Management's MCP server, and the app's agent API behind it.
/// Each test starts the two applications with the estate it reads, so none depends on another.
/// </summary>
public sealed class HealthCheckAgentTests
{
    private const string ToolsPath = "/mcp/health-check";
    private const string AgentApiPath = "/agent/v1/checks";
    private const string AgentViewFlow = "agent-view-health-check";
    private const string StandInFlow = "agent-mcp-health-check";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACallWithNoToken_IsRefused401()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.StandInClient(token: string.Empty);

        using var response = await CallAsync(client, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["messageId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ACallWithATokenTheStandInDoesNotTrust_IsRefused401()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.StandInClient(host.ForgedAgentToken());

        using var response = await CallAsync(client, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallWithATokenWithoutTheReadRole_IsRefused403()
    {
        await using var host = new HealthCheckAgentHost();
        using var client = host.StandInClient(host.AgentToken(roles: ["Something.Else"]));

        using var response = await CallAsync(client, "tools/list");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ACallOverTheRateLimit_IsRefused429WithRetryAfter()
    {
        await using var host = new HealthCheckAgentHost(rateLimitCalls: 2);
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
        await using var host = new HealthCheckAgentHost(rateLimitCalls: 1);
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
    public async Task TheAppsAgentApi_RefusesNoTokenAndAWrongRole_AndAnswersTheAgent()
    {
        await using var host = new HealthCheckAgentHost();
        using var anonymous = host.AppClient(token: string.Empty);
        using var forged = host.AppClient(host.ForgedAgentToken(HealthCheckAgentHost.ApiAudience));
        using var noRole = host.AppClient(host.AgentToken(roles: ["Something.Else"], audience: HealthCheckAgentHost.ApiAudience));
        using var agent = host.AppClient();

        (await anonymous.GetAsync(AgentApiPath, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await forged.GetAsync(AgentApiPath, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await noRole.GetAsync(AgentApiPath, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.GetAsync(AgentApiPath, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RunChecks_OnTheAsFoundSnapshot_ReturnsTheFindingForTheMissingDeadLetterAlerts()
    {
        await using var host = new HealthCheckAgentHost(snapshot: "as-found");
        using var client = host.StandInClient();

        var view = JsonNode.Parse(await ToolTextAsync(client, "run_checks"))!;

        view["estate"]!.GetValue<string>().Should().Be("as-found");
        var finding = view["findings"]!.AsArray().Should().ContainSingle().Subject!;
        finding["id"]!.GetValue<string>().Should().Be("F-DLQ-ALERT");
        finding["checkId"]!.GetValue<string>().Should().Be("DLQ-ALERT");
        finding["evidence"]!.AsArray().Select(item => item!["subject"]!.GetValue<string>()).Should().Contain("carrier-tracking");
    }

    [Fact]
    public async Task RunChecks_OnTheFixedSnapshot_ReturnsNoFinding()
    {
        await using var host = new HealthCheckAgentHost(snapshot: "fixed");
        using var client = host.StandInClient();

        var view = JsonNode.Parse(await ToolTextAsync(client, "run_checks"))!;

        view["estate"]!.GetValue<string>().Should().Be("fixed");
        view["checksRun"]!.GetValue<int>().Should().BeGreaterThan(0, "no Finding must mean every Check passed, not that none ran");
        view["findings"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task RunChecks_NeverCarriesASecretValue_InTheEvidenceTheAgentReads()
    {
        await using var host = new HealthCheckAgentHost(snapshot: "fixed", extraChecks: new LeakyCheck());
        using var client = host.StandInClient();

        var text = await ToolTextAsync(client, "run_checks");

        var finding = JsonNode.Parse(text)!["findings"]!.AsArray().Should().ContainSingle().Subject!;
        finding["checkId"]!.GetValue<string>().Should().Be(LeakyCheck.CheckId);
        text.Should().NotContain(LeakyCheck.SecretValue).And.NotContain("Bearer " + LeakyCheck.SecretValue);
        text.Should().Contain(EvidenceMasker.Mask, "the evidence says where the secret is and masks what it is");
    }

    [Fact]
    public async Task NarratingAHealthCheck_OnTheAsFoundSnapshot_CitesTheFindingAndPutsTheToolCallOnTheTrail_TaggedWithTheAgentAndTheTool()
    {
        await using var host = new HealthCheckAgentHost(snapshot: "as-found");
        var correlationId = Guid.NewGuid().ToString();

        var result = await NarrateAsync(host, correlationId);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value.Items.Select(item => (item.Id, item.ToolName)).Should().Equal([("F-DLQ-ALERT", "run_checks")]);
        var trail = host.Telemetry.Trail(correlationId);
        var calls = trail.Where(hop => hop.Flow is StandInFlow or AgentViewFlow).ToList();
        calls.Select(hop => hop.Flow).Should().BeEquivalentTo([StandInFlow, AgentViewFlow], "the tool call is a hop of the stand-in and of the app's agent view");
        calls.Should().OnlyContain(hop => hop.AgentId == HealthCheckAgentHost.AgentId && hop.ToolName == "run_checks" && hop.Outcome == "Succeeded");
    }

    [Fact]
    public async Task NarratingAHealthCheck_OnTheFixedSnapshot_ExplainsNothing()
    {
        await using var host = new HealthCheckAgentHost(snapshot: "fixed");

        var result = await NarrateAsync(host, Guid.NewGuid().ToString());

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value.Items.Should().BeEmpty();
        result.Value.Summary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AToolCallTheAgentViewRefuses_IsAgentToolRefused_AndTheAppSaysTheAssistantIsUnavailable()
    {
        await using var host = new HealthCheckAgentHost(appDistrustsStandIn: true);
        var correlationId = Guid.NewGuid().ToString();

        var result = await NarrateAsync(host, correlationId);

        // The page shows "Assistant unavailable" for any unsuccessful result, whatever the code.
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain("AgentToolRefused");
        host.Telemetry.Trail(correlationId).Should().Contain(
            hop => hop.Flow == StandInFlow && hop.ToolName == "run_checks" && hop.Outcome == "Refused" && hop.FailureCode == "AgentToolRefused" && hop.AgentId == HealthCheckAgentHost.AgentId,
            "the refusal is on the Message trail, tagged with the Agent and the tool");
    }

    /// <summary>A Check whose evidence lets a secret's value into its detail, which the checklist must mask on the way out.</summary>
    private sealed class LeakyCheck : Check
    {
        public const string CheckId = "SECRET-TEST";
        public const string SecretValue = "s3cr3t-do-not-leak";

        public override string Id => CheckId;

        public override string Rule => "No secret is in plain sight.";

        public override string Title => "A secret is in plain sight";

        public override Severity Severity => Severity.High;

        public override string AsFoundToggle => "none";

        public override string Fix => "Move the secret to Key Vault.";

        public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate) =>
            [new Evidence("carrier-tracking", $"sharedAccessKey={SecretValue}, sent as Bearer {SecretValue}")];
    }

    private static async Task<Result<Norvia.Agents.Narration>> NarrateAsync(HealthCheckAgentHost host, string correlationId)
    {
        using var scope = host.AppServices.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new NarrateHealthCheckQuery(correlationId), Ct);
    }

    private static Task<HttpResponseMessage> CallAsync(HttpClient client, string method, JsonObject? parameters = null) =>
        client.PostAsJsonAsync(
            new Uri(ToolsPath, UriKind.Relative),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters ?? new JsonObject() },
            Ct);

    /// <summary>Calls a tool and returns the text it answered with, the agent view's JSON.</summary>
    private static async Task<string> ToolTextAsync(HttpClient client, string tool)
    {
        using var response = await CallAsync(client, "tools/call", new JsonObject { ["name"] = tool, ["arguments"] = new JsonObject() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reply = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["result"]!;
        reply["isError"]!.GetValue<bool>().Should().BeFalse();
        return reply["content"]![0]!["text"]!.GetValue<string>();
    }
}
