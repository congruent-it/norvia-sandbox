using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents;
using static Norvia.Tms.Infrastructure.IntegrationTests.PlannerAgent.FakeResponses;

namespace Norvia.Tms.Infrastructure.IntegrationTests.PlannerAgent;

/// <summary>
/// The Foundry client (ADR 0040) as the TMS API builds it, over HTTP against a faked Responses endpoint: Suggest on a Transport Order asks the published
/// Planner agent, reads the answer and traces the run. Each test builds a host of its own.
/// </summary>
public sealed class FoundryAgentTests
{
    private const string Answer = """
        {"cause":"Own fleet is Over capacity that day, 34 of 30 pallet places; Kessler Spedition costs 412.50.","evidence":[{"claim":"Own fleet is OverCapacity on the collection day.","citation":"r1"},{"claim":"Kessler Spedition's Carrier cost is 412.50.","citation":"r1"}],"action":"needs-a-person","suggestedAction":"Assign to Carrier Kessler Spedition, accepting no warning.","confidence":"medium"}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Suggest_ReadsTheAnswer_WithTheOneAssignmentOptionsReadAsEvidence()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_assignment_options"), Message(Answer)]);

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value.Evidence.Should().HaveCount(2).And.OnlyContain(evidence => evidence.ToolName == "get_assignment_options" && evidence.ResultId == "r1");
        result.Value.Run!.AgentName.Should().Be("planner");
    }

    [Fact]
    public async Task Suggest_AsksThePublishedPlannerAgent_AsTheTmsApisIdentity_AtTheProjectsResponsesEndpoint()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_assignment_options"), Message(Answer)]);

        await host.Client.AskAsync(NewRequest(), Ct);

        var create = host.Responses.Requests.Single(recorded => recorded.Method == HttpMethod.Post);
        create.Uri.Should().Be($"{FoundryHost.Project}/openai/v1/responses");
        create.Authorization.Should().Be("Bearer foundry-token");
        JsonNode.Parse(create.Body)!["agent_reference"]!["name"]!.GetValue<string>().Should().Be("planner");
    }

    [Fact]
    public async Task Suggest_SendsTheOrdersCorrelationId_TheQuestionAndTheToolCallCap_AndStoresNothing()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_assignment_options"), Message(Answer)]);
        var request = NewRequest();

        await host.Client.AskAsync(request, Ct);

        var body = JsonNode.Parse(host.Responses.Requests.Single(recorded => recorded.Method == HttpMethod.Post).Body)!;
        body["structured_inputs"]!["correlationId"]!.GetValue<string>().Should().Be(request.CorrelationId);
        body["max_tool_calls"]!.GetValue<int>().Should().Be(1);
        body["store"]!.GetValue<bool>().Should().BeFalse();
        body["conversation"].Should().BeNull();
        body["input"]!.ToJsonString().Should().Contain("TO-0000042");
    }

    [Fact]
    public async Task Suggest_DeletesTheResponseAfterwards()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_assignment_options"), Message(Answer)]);

        await host.Client.AskAsync(NewRequest(), Ct);

        host.Responses.Requests.Should().Contain(recorded => recorded.Method == HttpMethod.Delete && recorded.Uri.EndsWith("/openai/v1/responses/resp-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Suggest_WhenTheResponseCannotBeDeleted_StillAnswers_AndSaysSoOnTheSpan()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_assignment_options"), Message(Answer)]);
        host.Responses.DeleteStatus = HttpStatusCode.InternalServerError;

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.IsSuccess.Should().BeTrue();
        host.Spans.Single().GetTagItem("failureCode.cleanup").Should().Be("ConversationNotDeleted");
    }

    [Fact]
    public async Task Suggest_TracesTheRunAsASpanOfTheTmsApi_WithTheCorrelationId()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_assignment_options"), Message(Answer)]);
        var request = NewRequest();

        await host.Client.AskAsync(request, Ct);

        var span = host.Spans.Single();
        span.OperationName.Should().Be("Agent run");
        span.GetTagItem("agent.name").Should().Be("planner");
        span.GetTagItem("correlationId").Should().Be(request.CorrelationId);
        span.GetTagItem("agent.response.id").Should().Be("resp-1");
        span.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task Suggest_WhenTheProjectAnswersAnError_IsUnavailable_WithoutARetry_AndTheSpanSaysSo()
    {
        await using var host = new FoundryHost();
        host.Responses.Fail(HttpStatusCode.ServiceUnavailable);

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain(AgentFailureCodes.AgentUnavailable);
        host.Responses.Requests.Should().ContainSingle(recorded => recorded.Method == HttpMethod.Post, "the standard resilience handler is removed from the Agent's client");
        var span = host.Spans.Single();
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task Suggest_WhenAToolWasRefused_IsAgentToolRefused()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_assignment_options", error: """{"code":403}"""), Message(Answer)]);

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task Suggest_WhenThePlannerCalledAnotherAgentsTool_IsAgentToolRefused()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_rejected_booking"), Message(Answer)]);

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.Errors.Should().Contain(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task Suggest_WhenThePlannerReadTwice_IsAgentToolRefused()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_assignment_options"), McpCall("get_assignment_options"), Message(Answer)]);

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.Errors.Should().Contain(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task Suggest_WhenTheProjectIsTooSlow_IsUnavailable_AfterTheTimeout()
    {
        await using var host = new FoundryHost(timeoutSeconds: 1);
        host.Responses.Hang = true;

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain(AgentFailureCodes.AgentUnavailable);
    }

    private static AgentRequest NewRequest() => new("planner", PlannerQuestions.SuggestAssignment("TO-0000042"), "corr-0001");
}
