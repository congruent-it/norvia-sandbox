using System.Net;
using System.Text.Json.Nodes;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.IntegrationConsole.Application.Agents.Explain;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Open;
using static Norvia.IntegrationConsole.Agents.IntegrationTests.FakeResponses;

namespace Norvia.IntegrationConsole.Agents.IntegrationTests;

/// <summary>
/// The Foundry client (ADR 0040) as the console builds it, over HTTP against a faked Responses endpoint: Explain on a Message trail asks the published
/// Agent, reads the answer and puts the run on the trail. Each test starts a console of its own.
/// </summary>
public sealed class FoundryAgentTests
{
    private const string Answer = """
        {"cause":"The Carrier sent DLV, which the map does not know.","evidence":[{"claim":"The tracking hop was dead-lettered.","citation":"r1"}],"suggestedAction":"Ask the Carrier to send POD.","confidence":"high"}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Explain_AsksThePublishedAgentOfTheProject_AsTheConsolesIdentity_WithTheTrailsCorrelationId_AndStoresNothing()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_message_trail"), Message(Answer)]);
        var correlationId = Guid.NewGuid().ToString();

        var result = await ExplainAsync(host, correlationId);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value.Cause.Should().Contain("DLV");
        result.Value.Evidence.Should().ContainSingle().Which.ToolName.Should().Be("get_message_trail");
        var create = host.Responses.Requests.Single(request => request.Method == HttpMethod.Post);
        create.Uri.Should().Be($"{FoundryHost.Project}/openai/v1/responses");
        create.Authorization.Should().Be("Bearer foundry-token");
        var body = JsonNode.Parse(create.Body)!;
        body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("integration-operations");
        body["structured_inputs"]!["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        body["store"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Explain_DeletesTheResponseAfterwards()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_message_trail"), Message(Answer)]);

        await ExplainAsync(host, Guid.NewGuid().ToString());

        host.Responses.Requests.Should().Contain(request => request.Method == HttpMethod.Delete && request.Uri.EndsWith("/openai/v1/responses/resp-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Explain_PutsTheRunOnTheTrail_AsAnAgentRunOfTheConsole()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_message_trail"), Message(Answer)]);
        var correlationId = Guid.NewGuid().ToString();

        await ExplainAsync(host, correlationId);

        var hops = await HopsAsync(host, correlationId);
        hops.Should().Contain(hop => hop.Flow == "integration-operations-agent" && hop.Outcome == Outcome.Succeeded);
    }

    [Fact]
    public async Task Explain_WhenTheProjectAnswersAnError_IsUnavailable_WithoutARetry_AndTheFailureIsOnTheTrail()
    {
        await using var host = new FoundryHost();
        host.Responses.Fail(HttpStatusCode.ServiceUnavailable);
        var correlationId = Guid.NewGuid().ToString();

        var result = await ExplainAsync(host, correlationId);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain("AgentUnavailable");
        host.Responses.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Post);
        (await HopsAsync(host, correlationId)).Should().Contain(hop => hop.Flow == "integration-operations-agent" && hop.Outcome == Outcome.Failed && hop.FailureCode == "AgentUnavailable");
    }

    [Fact]
    public async Task Explain_WhenAToolWasRefused_IsAgentToolRefused()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("get_message_trail", error: """{"code":403}"""), Message(Answer)]);

        var result = await ExplainAsync(host, Guid.NewGuid().ToString());

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain("AgentToolRefused");
    }

    [Fact]
    public async Task Explain_WhenTheAgentCalledAToolItsDefinitionDoesNotList_IsAgentToolRefused()
    {
        await using var host = new FoundryHost();
        host.Responses.Answer([McpCall("resubmit_message"), Message(Answer)]);

        var result = await ExplainAsync(host, Guid.NewGuid().ToString());

        result.Errors.Should().Contain("AgentToolRefused");
    }

    [Fact]
    public async Task Explain_WhenTheProjectIsTooSlow_IsUnavailable_AfterTheTimeout()
    {
        await using var host = new FoundryHost(timeoutSeconds: 1);
        host.Responses.Hang = true;

        var result = await ExplainAsync(host, Guid.NewGuid().ToString());

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Contain("AgentUnavailable");
    }

    private static async Task<Result<Norvia.Agents.Suggestion>> ExplainAsync(FoundryHost host, string correlationId)
    {
        using var scope = host.ConsoleServices.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ExplainTrailQuery(correlationId), Ct);
    }

    private static async Task<IReadOnlyList<TrailHop>> HopsAsync(FoundryHost host, string correlationId)
    {
        using var scope = host.ConsoleServices.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new OpenTrailQuery(correlationId), Ct);
        return result.IsSuccess ? result.Value.Hops : [];
    }
}
