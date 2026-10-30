// NSubstitute sets up a ValueTask-returning call by calling it and configuring its result; nothing is left unawaited.
#pragma warning disable CA2012
using System.Net;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Norvia.HealthCheck.Application.Checks;
using Norvia.HealthCheck.Application.Failures;
using Norvia.HealthCheck.Application.Run;
using Norvia.HealthCheck.Web.Agents;
using TestContext = Xunit.TestContext;

namespace Norvia.HealthCheck.Web.UnitTests.Agents;

public sealed class AgentApiTests : IAsyncLifetime
{
    private static readonly HealthCheckReport AsFound = new(
        "as-found",
        new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero),
        1,
        [
            new Finding(
                "F-DLQ-ALERT",
                "DLQ-ALERT",
                "A dead-letter queue has no alert",
                "Every queue has an alert.",
                Severity.High,
                [new Evidence("carrier-tracking", "No alert watches carrier-tracking."), new Evidence("billing", "No alert watches billing.")],
                "entitiesWithoutDeadLetterAlert",
                "Remove it from the toggle."),
        ]);

    private AgentApiHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await AgentApiHost.StartAsync();
        _host.Mediator.Send(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(AsFound)));
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task GetChecks_WithAnAgentsToken_AnswersTheFindingsWithTheChecksOwnSeverityAndFix()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        view["estate"]!.GetValue<string>().Should().Be("as-found");
        view["checksRun"]!.GetValue<int>().Should().Be(1);
        var finding = view["findings"]!.AsArray().Should().ContainSingle().Subject!;
        finding["id"]!.GetValue<string>().Should().Be("F-DLQ-ALERT");
        finding["checkId"]!.GetValue<string>().Should().Be("DLQ-ALERT");
        finding["severity"]!.GetValue<string>().Should().Be("High");
        finding["asFoundToggle"]!.GetValue<string>().Should().Be("entitiesWithoutDeadLetterAlert");
        finding["fix"]!.GetValue<string>().Should().Be("Remove it from the toggle.");
        finding["evidence"]!.AsArray().Select(item => item!["subject"]!.GetValue<string>()).Should().Equal("carrier-tracking", "billing");
        // The Agent follows a run the page has just made, so it asks for that run's report if it is under a minute old.
        await _host.Mediator.Received(1).Send(new RunHealthCheckQuery(ReuseWithin: TimeSpan.FromSeconds(60)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFailedRuns_AnswersTheFlowsThatFailed_WithWhetherAnAlertWatchesEach()
    {
        _host.Mediator.Send(Arg.Any<ListFailedRunsQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(new FailedRunsReport("as-found", 4, [new FailedFlow("carrier-tracking", 14, false)]))));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/failed-runs", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        view["windowDays"]!.GetValue<int>().Should().Be(4);
        var flow = view["flows"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        flow.Select(property => property.Key).Should().BeEquivalentTo("flow", "failedRuns", "hasFailedRunAlert");
        flow["flow"]!.GetValue<string>().Should().Be("carrier-tracking");
        flow["failedRuns"]!.GetValue<int>().Should().Be(14);
        flow["hasFailedRunAlert"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task GetDeadLetterQueues_AnswersTheEntitiesThatHoldMessages_WithWhetherAnAlertWatchesEach()
    {
        _host.Mediator.Send(Arg.Any<ListDeadLetterQueuesQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(new DeadLetterQueuesReport("as-found", [new DeadLetteredEntity("carrier-tracking", 3, false)]))));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/dead-letter-queues", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var queue = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["queues"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        queue.Select(property => property.Key).Should().BeEquivalentTo("name", "deadLetteredMessages", "hasDeadLetterAlert");
        queue["deadLetteredMessages"]!.GetValue<int>().Should().Be(3);
    }

    [Theory]
    [InlineData("/agent/v1/failed-runs")]
    [InlineData("/agent/v1/dead-letter-queues")]
    public async Task TheListRoutes_WithoutTheAgentRole_AreRefused(string path)
    {
        using var none = await _host.Client.SendAsync(AgentApiHost.Get(path), TestContext.Current.CancellationToken);
        using var operatorRole = await _host.Client.SendAsync(AgentApiHost.Get(path, _host.Token(roles: ["HealthCheck.Read"])), TestContext.Current.CancellationToken);

        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        operatorRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetChecks_OverTheFixedEstate_AnswersAnEmptyList()
    {
        _host.Mediator.Send(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(new HealthCheckReport("fixed", DateTimeOffset.UnixEpoch, 1, []))));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks", _host.Token()), TestContext.Current.CancellationToken);

        var view = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        view["findings"]!.AsArray().Should().BeEmpty();
        view["checksRun"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task GetChecks_NeverCarriesAFieldTheViewDoesNotListNorASecret()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks", _host.Token()), TestContext.Current.CancellationToken);

        var finding = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["findings"]![0]!.AsObject();
        finding.Select(property => property.Key).Should().BeEquivalentTo("id", "checkId", "title", "rule", "severity", "evidence", "asFoundToggle", "fix");
        finding["evidence"]![0]!.AsObject().Select(property => property.Key).Should().BeEquivalentTo("subject", "detail");
    }

    [Fact]
    public async Task GetChecks_WithoutAToken_IsRefused401_EvenForTheOperatorTheAppSignsInLocally()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: "the operators' roles open nothing on the agent API");
        await _host.Mediator.DidNotReceiveWithAnyArgs().Send(default(RunHealthCheckQuery)!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("HealthCheck.Run")]
    [InlineData("HealthCheck.Read")]
    [InlineData("IntegrationConsole.Agent.Read")]
    public async Task GetChecks_WithATokenWithoutTheAgentRole_IsRefused403(string role)
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks", _host.Token(roles: [role])), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _host.Mediator.DidNotReceiveWithAnyArgs().Send(default(RunHealthCheckQuery)!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetChecks_WithATokenForAnotherAudience_OrSignedWithAnotherKey_IsRefused401()
    {
        using var otherAudience = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks", _host.Token(audience: "api://norvia-integration-console-agent-api")), TestContext.Current.CancellationToken);
        using var otherKey = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks", _host.Token(key: new byte[64])), TestContext.Current.CancellationToken);

        otherAudience.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        otherKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void TheAgentApi_HasOnlyReadRoutes_ThatRequireTheAgentRole()
    {
        var routes = _host.AgentRoutes.ToList();

        routes.Select(route => route.RoutePattern.RawText).Should().BeEquivalentTo("/agent/v1/checks", "/agent/v1/failed-runs", "/agent/v1/dead-letter-queues");
        routes.SelectMany(route => route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Should().OnlyContain(method => method == "GET");
        routes.Should().OnlyContain(route => route.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any(data => data.Policy == "HealthCheck.Agent.Read"));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task AWriteToTheAgentApi_IsNotServed_EvenWithTheAgentsToken(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/agent/v1/checks");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _host.Token());

        using var response = await _host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetChecks_WithTheCorrelationIdOfARun_IsAHopOfTheAppsOnThatTrail_TaggedWithTheTokensAgentAndTheTool()
    {
        using var _ = await _host.Client.SendAsync(
            AgentApiHost.Get("/agent/v1/checks", _host.Token(), ("X-Correlation-ID", "run-9"), ("X-Tool-Name", "run_checks"), ("X-Agent-ID", "someone-else")),
            TestContext.Current.CancellationToken);

        var keys = LastOutcomeKeys(_host);
        keys["correlationId"].Should().Be("run-9");
        keys["flow"].Should().Be("agent-view-health-check");
        keys["agentId"].Should().Be(AgentApiHost.AgentId, because: "the identity is the validated token's, never a header's (ADR 0005)");
        keys["toolName"].Should().Be("run_checks");
        keys["outcome"].Should().Be("Succeeded");
    }

    [Fact]
    public async Task GetChecks_ThroughTheGateway_IsAHopTaggedWithTheAgentTheGatewayNames()
    {
        await using var behindApim = await AgentApiHost.StartAsync(gatewayClientId: "apim-identity");
        behindApim.Mediator.Send(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(AsFound)));

        using var _ = await behindApim.Client.SendAsync(
            AgentApiHost.Get("/agent/v1/checks", behindApim.Token(azp: "apim-identity"), ("X-Correlation-ID", "run-9"), ("X-Agent-ID", "foundry-agent-identity"), ("X-Tool-Name", "run_checks")),
            TestContext.Current.CancellationToken);

        LastOutcomeKeys(behindApim)["agentId"].Should().Be("foundry-agent-identity", because: "the gateway's policy sets the header from the Agent's own validated token");
    }

    [Fact]
    public async Task GetChecks_ByAnyoneButTheGateway_IsTaggedWithTheTokensAgent_NotTheHeader()
    {
        await using var behindApim = await AgentApiHost.StartAsync(gatewayClientId: "apim-identity");
        behindApim.Mediator.Send(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(AsFound)));

        using var _ = await behindApim.Client.SendAsync(
            AgentApiHost.Get("/agent/v1/checks", behindApim.Token(azp: "some-other-caller"), ("X-Correlation-ID", "run-9"), ("X-Agent-ID", "foundry-agent-identity")),
            TestContext.Current.CancellationToken);

        LastOutcomeKeys(behindApim)["agentId"].Should().Be("some-other-caller");
    }

    [Fact]
    public async Task InAzure_TheTokensAreEntrasNotTheLocalKeys_SoALocallySignedTokenIsRefused()
    {
        await using var azure = await AgentApiHost.StartAsync(entra: true);

        using var response = await azure.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks", azure.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetChecks_WithoutACorrelationId_IsOnNoTrail_ButStillAnswers()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _host.Logs.GetSnapshot()
            .Where(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).Any(scope => scope.Key == "flow"))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task AgentsOff_TheAgentApiIsNotWired()
    {
        await using var off = await AgentApiHost.StartAsync(enabled: false, withKey: false);

        AgentApiAuthentication.IsEnabled(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agents:Enabled"] = "false" }).Build()).Should().BeFalse();
        using var response = await off.Client.SendAsync(AgentApiHost.Get("/agent/v1/checks", off.Token()), TestContext.Current.CancellationToken);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AgentsOn_WithoutTheTokenSettings_DoNotStart()
    {
        var start = () => AgentApiHost.StartAsync(enabled: true, withKey: false);

        await start.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>();
    }

    private static Dictionary<string, object?> LastOutcomeKeys(AgentApiHost host) =>
        host.Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary())
            .Last(scope => scope.ContainsKey("outcome"));
}
