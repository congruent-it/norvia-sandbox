using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.IntegrationConsole.Application.Agents;
using Norvia.IntegrationConsole.Application.Agents.Explain;

namespace Norvia.IntegrationConsole.Application.UnitTests.Agents;

public sealed class ExplainTrailHandlerTests
{
    private const string CorrelationId = "5b0d7f8e-2c41-4a3e-9d6a-0f1e2d3c4b5a";

    private static readonly Suggestion Answer = new(
        "The Carrier-tracking flow could not map the status code.",
        [new Evidence("The hop ended DeadLettered.", "r1", "get_message_trail")],
        "Ask the Carrier to resend.",
        Confidence.High);

    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    [Fact]
    public async Task Handle_ForATrail_AsksTheIntegrationOperationsAgentAboutThatTrail()
    {
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Answer);

        var result = await new ExplainTrailHandler(_agent).Handle(new ExplainTrailQuery($" {CorrelationId} "), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Answer);
        await _agent.Received(1).AskAsync(
            Arg.Is<AgentRequest>(request => request.AgentName == "integration-operations" && request.CorrelationId == CorrelationId && request.Question.Contains(CorrelationId, StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheQuestionIsTheSameEveryTime_SoTheOperatorAsksNothingFreeForm()
    {
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Answer);
        var handler = new ExplainTrailHandler(_agent);

        await handler.Handle(new ExplainTrailQuery("c-1"), TestContext.Current.CancellationToken);
        await handler.Handle(new ExplainTrailQuery("c-2"), TestContext.Current.CancellationToken);

        var questions = _agent.ReceivedCalls().Select(call => ((AgentRequest)call.GetArguments()[0]!).Question).ToList();
        questions[0].Replace("c-1", "#", StringComparison.Ordinal).Should().Be(questions[1].Replace("c-2", "#", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_TheAgentIsUnavailable_PassesTheFailureCodeOn()
    {
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentToolRefused));

        var result = await new ExplainTrailHandler(_agent).Handle(new ExplainTrailQuery(CorrelationId), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WithoutACorrelationId_IsInvalid_AndTheAgentIsNotAsked(string correlationId)
    {
        var result = await new ExplainTrailHandler(_agent).Handle(new ExplainTrailQuery(correlationId), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }
}
