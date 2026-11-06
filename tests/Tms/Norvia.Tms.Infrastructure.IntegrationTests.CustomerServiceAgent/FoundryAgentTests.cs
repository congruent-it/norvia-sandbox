using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents;
using static Norvia.Tms.Infrastructure.IntegrationTests.CustomerServiceAgent.FakeResponses;

namespace Norvia.Tms.Infrastructure.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// The Foundry client (ADR 0040) as the TMS UI builds it, over HTTP against a faked Responses endpoint: Explain on a Rejected booking asks the published
/// Agent, reads the answer and traces the run. Each test builds a host of its own.
/// </summary>
public sealed class FoundryAgentTests
{
    private const string Answer = """
        {"cause":"The booking has a weight of 0 kg, which is not allowed.","evidence":[{"claim":"The booking was rejected with WeightInvalid.","citation":"r1"}],"action":"needs-a-person","suggestedAction":"Ask the Shipper to give the goods a weight above 0 kg.","confidence":"high"}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Explain_AsksThePublishedAgentOfTheProject_AsTheTmsUisIdentity_WithTheBookingsCorrelationId_AndStoresNothing()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_rejected_booking"), Message(Answer)]);
        var request = NewRequest();

        var result = await host.Client.AskAsync(request, Ct);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value.Cause.Should().Contain("weight");
        result.Value.Evidence.Should().ContainSingle().Which.ToolName.Should().Be("get_rejected_booking");
        result.Value.Run!.AgentName.Should().Be("customer-service");
        var create = host.Responses.Requests.Single(recorded => recorded.Method == HttpMethod.Post);
        create.Uri.Should().Be($"{FoundryHost.Project}/openai/v1/responses");
        create.Authorization.Should().Be("Bearer foundry-token");
        var body = JsonNode.Parse(create.Body)!;
        body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("customer-service");
        body["structured_inputs"]!["correlationId"]!.GetValue<string>().Should().Be(request.CorrelationId);
        body["store"]!.GetValue<bool>().Should().BeFalse();
        body["conversation"].Should().BeNull();
    }

    [Fact]
    public async Task Explain_DeletesTheResponseAfterwards()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_rejected_booking"), Message(Answer)]);

        await host.Client.AskAsync(NewRequest(), Ct);

        host.Responses.Requests.Should().Contain(recorded => recorded.Method == HttpMethod.Delete && recorded.Uri.EndsWith("/openai/v1/responses/resp-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Explain_WhenTheResponseCannotBeDeleted_StillAnswers_AndSaysSoOnTheSpan()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_rejected_booking"), Message(Answer)]);
        host.Responses.DeleteStatus = HttpStatusCode.InternalServerError;

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.IsSuccess.Should().BeTrue();
        host.Spans.Single().GetTagItem("failureCode.cleanup").Should().Be("ConversationNotDeleted");
    }

    [Fact]
    public async Task Explain_TracesTheRunAsASpanOfTheTmsUi_WithTheCorrelationId()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_rejected_booking"), Message(Answer)]);
        var request = NewRequest();

        await host.Client.AskAsync(request, Ct);

        var span = host.Spans.Single();
        span.OperationName.Should().Be("Agent run");
        span.GetTagItem("agent.name").Should().Be("customer-service");
        span.GetTagItem("correlationId").Should().Be(request.CorrelationId);
        span.GetTagItem("agent.response.id").Should().Be("resp-1");
        span.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task Explain_WhenTheProjectAnswersAnError_IsUnavailable_WithoutARetry_AndTheSpanSaysSo()
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
    public async Task Explain_WhenAToolWasRefused_IsAgentToolRefused()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_rejected_booking", error: """{"code":403}"""), Message(Answer)]);

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task Explain_WhenTheAgentCalledAToolItsDefinitionDoesNotList_IsAgentToolRefused()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_shipper"), Message(Answer)]);

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.Errors.Should().Contain(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task Explain_WhenTheProjectIsTooSlow_IsUnavailable_AfterTheTimeout()
    {
        await using var host = new FoundryHost(timeoutSeconds: 1);
        host.Responses.Hang = true;

        var result = await host.Client.AskAsync(NewRequest(), Ct);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain(AgentFailureCodes.AgentUnavailable);
    }

    private static AgentRequest NewRequest()
    {
        var id = Guid.NewGuid();
        return new AgentRequest("customer-service", CustomerServiceQuestions.ExplainRejectedBooking(id), Guid.NewGuid().ToString());
    }
}
