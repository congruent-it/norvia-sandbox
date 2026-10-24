// NSubstitute sets up a ValueTask-returning call by calling it and configuring its result; nothing is left unawaited.
#pragma warning disable CA2012
using System.Net;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.List;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.Runs;
using Norvia.IntegrationConsole.Application.Runs.Failed;
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

    private static readonly DeadLetteredMessageList Peeked = new(
        "shipment-events/billing",
        [
            new RecordedDeadLetter(7, Now.AddHours(-2), "UnmappableTrackingMessage", "Unknown status code DLV", 1, "c-1",
                [new BusinessKey(BusinessKeyKind.ShipmentNumber, "SH-0000007")], new Dictionary<string, string> { ["correlationId"] = "c-1", ["statusCode"] = "DLV" }),
            new RecordedDeadLetter(8, Now.AddHours(-1), null, null, 10, null, []),
        ],
        HasMore: false);

    [Fact]
    public async Task ListFailedRuns_AnswersEachRunWithItsFlowStepAndError_AndNoBody()
    {
        _host.Mediator.Send(Arg.Any<ListFailedRunsQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new FailedRunList(
            Now.AddHours(-24),
            Now,
            ["carrier-booking"],
            [new FailedRun("carrier-booking", new WorkflowRun("logic-booking", "carrier-booking", "08584"), Now.AddMinutes(-5), "c-9", "Call_Carrier", new StepError("BadRequest", "refused"))])));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/failed-runs", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var run = JsonNode.Parse(text)!["runs"]![0]!;
        run["flow"]!.GetValue<string>().Should().Be("carrier-booking");
        run["failedStep"]!.GetValue<string>().Should().Be("Call_Carrier");
        run["error"]!["code"]!.GetValue<string>().Should().Be("BadRequest");
        run["correlationId"]!.GetValue<string>().Should().Be("c-9");
        run["run"]!["runId"]!.GetValue<string>().Should().Be("08584");
        text.Should().NotContainEquivalentOf("body").And.NotContainEquivalentOf("input").And.NotContainEquivalentOf("output");
        await _host.Mediator.Received(1).Send(new ListFailedRunsQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListDeadLetterQueues_AnswersTheCountAndTheAgeOfTheOldestMessage_OfEachQueue()
    {
        _host.Mediator.Send(Arg.Any<ListDeadLetterQueuesQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new DeadLetterQueueList(
            Now,
            [new DeadLetterEntity("carrier-tracking", 3, TimeSpan.FromHours(2)), new DeadLetterEntity("transport-orders", 0, null)])));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/dead-letter-queues", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var queues = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["queues"]!.AsArray();
        queues[0]!["name"]!.GetValue<string>().Should().Be("carrier-tracking");
        queues[0]!["count"]!.GetValue<long>().Should().Be(3);
        queues[0]!["oldestAgeSeconds"]!.GetValue<double>().Should().Be(7200);
        queues[1]!["oldestAgeSeconds"].Should().BeNull();
    }

    [Theory]
    [InlineData("/agent/v1/dead-letter-queues/shipment-events%2Fbilling/messages")]
    [InlineData("/agent/v1/dead-letter-queues/shipment-events/billing/messages")]
    public async Task PeekDeadLetters_ForASubscription_PeeksItsMessages_AnswersWhatTheToolDescribes_AndNoBody(string path)
    {
        _host.Mediator.Send(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(Peeked)));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get(path, _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var message = JsonNode.Parse(text)!["messages"]![0]!;
        message["sequenceNumber"]!.GetValue<long>().Should().Be(7);
        message["reason"]!.GetValue<string>().Should().Be("UnmappableTrackingMessage");
        message["description"]!.GetValue<string>().Should().Be("Unknown status code DLV");
        message["deliveryCount"]!.GetValue<int>().Should().Be(1);
        message["correlationId"]!.GetValue<string>().Should().Be("c-1");
        message["businessKeys"]![0]!["value"]!.GetValue<string>().Should().Be("SH-0000007");
        message["applicationProperties"]!["statusCode"]!.GetValue<string>().Should().Be("DLV");
        text.Should().NotContainEquivalentOf("body");
        await _host.Mediator.Received(1).Send(new ListDeadLetteredMessagesQuery("shipment-events/billing"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PeekDeadLetters_OnlyEverPeeks_SoTheQueueIsUnchanged()
    {
        _host.Mediator.Send(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(Peeked)));

        using var _ = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/dead-letter-queues/carrier-tracking/messages", _host.Token()), TestContext.Current.CancellationToken);

        // The one request the view makes is the peek's query: it sends no command, which is all that could receive, resubmit or discard.
        _host.Mediator.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[0].Should().BeOfType<ListDeadLetteredMessagesQuery>();
    }

    [Fact]
    public async Task PeekDeadLetters_ForAQueueHoldingMoreThanTheLimit_ShowsTheOldest_AndSaysThereIsMore()
    {
        var many = new DeadLetteredMessageList(
            "carrier-tracking",
            [.. Enumerable.Range(1, AgentTrailEndpoints.PeekLimit + 5).Select(number => new RecordedDeadLetter(number, Now, "r", null, 1, null, []))],
            HasMore: false);
        _host.Mediator.Send(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(many)));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/dead-letter-queues/carrier-tracking/messages", _host.Token()), TestContext.Current.CancellationToken);

        var view = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        view["messages"]!.AsArray().Should().HaveCount(AgentTrailEndpoints.PeekLimit);
        view["hasMore"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task PeekDeadLetters_ForAQueueTheEstateDoesNotHave_Is404()
    {
        _host.Mediator.Send(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result<DeadLetteredMessageList>.NotFound()));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/dead-letter-queues/nothing/messages", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PeekDeadLetters_WhenTheUseCaseRefusesTheInput_IsAProblem400()
    {
        _host.Mediator.Send(Arg.Any<ListDeadLetteredMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result<DeadLetteredMessageList>.Invalid(new ValidationError("Entity", "Open a dead-letter queue by its queue or subscription name."))));

        using var response = await _host.Client.SendAsync(AgentApiHost.Get("/agent/v1/dead-letter-queues/%20/messages", _host.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/agent/v1/failed-runs")]
    [InlineData("/agent/v1/dead-letter-queues")]
    [InlineData("/agent/v1/dead-letter-queues/carrier-tracking/messages")]
    public async Task TheNewTools_WithoutTheAgentsToken_AreRefused401(string path)
    {
        using var response = await _host.Client.SendAsync(AgentApiHost.Get(path), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

        routes.Should().HaveCount(6);
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
    public async Task Get_ThroughTheGateway_IsAHopTaggedWithTheAgentTheGatewayNames()
    {
        await using var behindApim = await AgentApiHost.StartAsync(gatewayClientId: "apim-identity");
        behindApim.Mediator.Send(Arg.Any<OpenTrailQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(Trail)));
        behindApim.Mediator.Send(Arg.Any<OpenRunQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(Run)));

        using var _ = await behindApim.Client.SendAsync(
            AgentApiHost.Get("/agent/v1/trails/c-1", behindApim.Token(azp: "apim-identity"), ("X-Correlation-ID", "run-trail-9"), ("X-Agent-ID", "foundry-agent-identity"), ("X-Tool-Name", "get_run")),
            TestContext.Current.CancellationToken);

        behindApim.Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary())
            .Last(scope => scope.ContainsKey("outcome"))["agentId"].Should().Be("foundry-agent-identity", because: "the gateway's policy sets the header from the Agent's own validated token");
    }

    [Fact]
    public async Task Get_ByAnyoneButTheGateway_IsTaggedWithTheTokensAgent_NotTheHeader()
    {
        await using var behindApim = await AgentApiHost.StartAsync(gatewayClientId: "apim-identity");
        behindApim.Mediator.Send(Arg.Any<OpenTrailQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(Trail)));
        behindApim.Mediator.Send(Arg.Any<OpenRunQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(Run)));

        using var _ = await behindApim.Client.SendAsync(
            AgentApiHost.Get("/agent/v1/trails/c-1", behindApim.Token(azp: "some-other-caller"), ("X-Correlation-ID", "run-trail-9"), ("X-Agent-ID", "foundry-agent-identity")),
            TestContext.Current.CancellationToken);

        behindApim.Logs.GetSnapshot()
            .Select(log => log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary())
            .Last(scope => scope.ContainsKey("outcome"))["agentId"].Should().Be("some-other-caller");
    }

    [Fact]
    public async Task InAzure_TheTokensAreEntrasNotTheLocalKeys_SoALocallySignedTokenIsRefused()
    {
        await using var azure = await AgentApiHost.StartAsync(entra: true);

        using var response = await azure.Client.SendAsync(AgentApiHost.Get("/agent/v1/trails/c-1", azure.Token()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
        off.AgentRoutes.Should().HaveCount(6, because: "the host maps it as Program.cs does only when enabled; this host maps it regardless to show the token scheme is absent");
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
