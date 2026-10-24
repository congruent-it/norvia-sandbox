// NSubstitute sets up a ValueTask-returning call by calling it and configuring its result; nothing is left unawaited.
#pragma warning disable CA2012
using System.Net;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Runs.Open;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Open;
using Norvia.IntegrationConsole.Web.Agents;
using TestContext = Xunit.TestContext;

namespace Norvia.IntegrationConsole.Web.UnitTests.Agents;

public sealed class AgentApiTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private AgentApiHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await AgentApiHost.StartAsync();
        _host.Mediator.Send(Arg.Any<OpenTrailQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(Trail)));
        _host.Mediator.Send(Arg.Any<OpenRunQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(Run)));
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static MessageTrail Trail => new(
        "c-1",
        Now.AddDays(-90),
        Now,
        [
            new TrailHop("partner-api: POST /v1/tracking", "carrier-tracking", Now, TimeSpan.FromMilliseconds(40), Outcome.Succeeded, null, 0, null),
            new TrailHop(
                "logic-tracking: carrier-tracking",
                "carrier-tracking",
                Now.AddSeconds(2),
                TimeSpan.FromSeconds(1.5),
                Outcome.DeadLettered,
                "UnmappableTrackingMessage",
                0,
                new WorkflowRun("logic-tracking", "carrier-tracking", "0858"),
                new DeadLetterHandling(DeadLetterStatus.Discarded, "carrier-tracking", null, "ana@norvia.example", Now, "Carrier resent it"),
                Fault: "carrier-tracking-unmapped-code"),
        ],
        [new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0000007")]);

    private static RunDetail Run => new(
        new WorkflowRun("logic-tracking", "carrier-tracking", "0858"),
        IsKept: true,
        "Failed",
        Now,
        TimeSpan.FromSeconds(2),
        [
            new RunStep("Trigger", "Succeeded", Now, TimeSpan.FromMilliseconds(5), null, InputsHidden: true, OutputsHidden: false),
            new RunStep("Map_status_code", "Failed", Now.AddSeconds(1), TimeSpan.FromSeconds(1), new StepError("InvalidTemplate", "Unknown status code DLV"), InputsHidden: false, OutputsHidden: true),
        ],
        RunHistoryDays: 90);

    [Fact]
    public async Task GetTrail_WithAnAgentsToken_AnswersTheNarrowView_WithStatusesCodesAndBusinessKeys()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/c-1", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        view["correlationId"]!.GetValue<string>().Should().Be("c-1");
        var hops = view["hops"]!.AsArray();
        hops.Should().HaveCount(2);
        hops[1]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        hops[1]!["failureCode"]!.GetValue<string>().Should().Be("UnmappableTrackingMessage");
        hops[1]!["fault"]!.GetValue<string>().Should().Be("carrier-tracking-unmapped-code");
        hops[1]!["durationMs"]!.GetValue<double>().Should().Be(1500);
        hops[1]!["run"]!["runId"]!.GetValue<string>().Should().Be("0858");
        hops[1]!["deadLetter"]!.GetValue<string>().Should().Be("Discarded");
        view["businessKeys"]![0]!["kind"]!.GetValue<string>().Should().Be("ShipmentNumber");
        await _host.Mediator.Received(1).Send(new OpenTrailQuery("c-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTrail_NeverCarriesWhoHandledAMessageOrWhy_OrAnyBody()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/c-1", _host.Token()), TestContext.Current.CancellationToken);

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Should().NotContain("ana@norvia.example").And.NotContain("Carrier resent it").And.NotContainEquivalentOf("body");
    }

    [Fact]
    public async Task GetRun_WithAnAgentsToken_AnswersStatusAndStepsWithTheirErrors_NeverTheirInputsOrOutputs()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/runs/logic-tracking/carrier-tracking/0858", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var view = JsonNode.Parse(text)!;
        view["status"]!.GetValue<string>().Should().Be("Failed");
        view["isKept"]!.GetValue<bool>().Should().BeTrue();
        var steps = view["steps"]!.AsArray();
        steps.Select(step => step!["name"]!.GetValue<string>()).Should().Equal("Trigger", "Map_status_code");
        steps[1]!["error"]!["code"]!.GetValue<string>().Should().Be("InvalidTemplate");
        steps[1]!["error"]!["message"]!.GetValue<string>().Should().Be("Unknown status code DLV");
        text.Should().NotContainEquivalentOf("input").And.NotContainEquivalentOf("output").And.NotContainEquivalentOf("hidden");
        await _host.Mediator.Received(1).Send(new OpenRunQuery("logic-tracking", "carrier-tracking", "0858"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRun_ARunHistoryNoLongerKeeps_SaysSo()
    {
        _host.Mediator.Send(Arg.Any<OpenRunQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(new RunDetail(new WorkflowRun("a", "b", "c"), IsKept: false, null, null, null, [], 90))));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/runs/a/b/c", _host.Token()), TestContext.Current.CancellationToken);

        var view = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        view["isKept"]!.GetValue<bool>().Should().BeFalse();
        view["steps"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Get_WhenTheUseCaseRefusesTheInput_IsAProblem400()
    {
        _host.Mediator.Send(Arg.Any<OpenTrailQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result<MessageTrail>.Invalid(new ValidationError("CorrelationId", "Open a Message trail by its correlation ID."))));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/%20", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Open a Message trail by its correlation ID.");
    }

    [Fact]
    public async Task Get_WhenTheRunUseCaseRefusesTheInput_IsAProblem400()
    {
        _host.Mediator.Send(Arg.Any<OpenRunQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result<RunDetail>.Invalid(new ValidationError("RunId", "Open a run by its Logic App, workflow and run ID."))));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/runs/a/b/%20", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_WithoutAToken_IsRefused401_EvenForTheOperatorTheConsoleSignsInLocally()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/c-1"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because: "the operators' roles open nothing on the agent API");
        await _host.Mediator.DidNotReceiveWithAnyArgs().Send(default(OpenTrailQuery)!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Get_WithATokenWithoutTheAgentRole_IsRefused403()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/c-1", _host.Token(roles: ["IntegrationConsole.Read"])), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_WithATokenForAnotherAudience_OrSignedWithAnotherKey_IsRefused401()
    {
        using var otherAudience = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/c-1", _host.Token(audience: "api://another")), TestContext.Current.CancellationToken);
        using var otherKey = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/c-1", _host.Token(key: new byte[64])), TestContext.Current.CancellationToken);

        otherAudience.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        otherKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void TheAgentApi_HasNoRouteThatWrites_AndEveryRouteRequiresTheAgentRole()
    {
        var routes = _host.AgentRoutes.ToList();

        routes.Should().HaveCount(2);
        routes.SelectMany(route => route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Should().OnlyContain(method => method == "GET");
        routes.Should().OnlyContain(route => route.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any(data => data.Policy == "IntegrationConsole.Agent.Read"));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task AWriteToTheAgentApi_IsNotServed_EvenWithTheAgentsToken(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/agent/v1/trails/c-1");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _host.Token());

        using var response = await _host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WithTheCorrelationIdOfARun_IsAHopOfTheConsolesOnThatTrail_TaggedWithTheTokensAgentAndTheTool()
    {
        using var _ = await _host.Client.SendAsync(
            AgentApiHost.Get("/agent/v1/trails/c-1", _host.Token(), ("X-Correlation-ID", "run-trail-9"), ("X-Tool-Name", "get_message_trail"), ("X-Agent-ID", "someone-else")),
            TestContext.Current.CancellationToken);

        var keys = _host.Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary())
            .Last(scope => scope.ContainsKey("outcome"));
        keys["correlationId"].Should().Be("run-trail-9");
        keys["flow"].Should().Be("agent-view-integration-operations");
        keys["agentId"].Should().Be("norvia-local-agent-integration-operations", because: "the identity is the validated token's, never a header's (ADR 0005)");
        keys["toolName"].Should().Be("get_message_trail");
        keys["outcome"].Should().Be("Succeeded");
    }

    [Fact]
    public async Task Get_ThatTheUseCaseRefused_IsARefusedHop()
    {
        _host.Mediator.Send(Arg.Any<OpenTrailQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result<MessageTrail>.Invalid(new ValidationError("CorrelationId", "no"))));

        using var _ = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/x", _host.Token(), ("X-Correlation-ID", "run-trail-9")), TestContext.Current.CancellationToken);

        _host.Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary())
            .Last(scope => scope.ContainsKey("outcome"))["outcome"].Should().Be("Refused");
    }

    [Fact]
    public async Task Get_WithoutACorrelationId_IsOnNoTrail_ButStillAnswers()
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/c-1", _host.Token()), TestContext.Current.CancellationToken);

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
        off.AgentRoutes.Should().HaveCount(2, because: "the host maps it as Program.cs does only when enabled; this host maps it regardless to show the token scheme is absent");
        using var response = await off.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/c-1", off.Token()), TestContext.Current.CancellationToken);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AgentsOn_WithoutTheTokenSettings_DoNotStart()
    {
        var start = () => AgentApiHost.StartAsync(enabled: true, withKey: false);

        await start.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>();
    }
}
