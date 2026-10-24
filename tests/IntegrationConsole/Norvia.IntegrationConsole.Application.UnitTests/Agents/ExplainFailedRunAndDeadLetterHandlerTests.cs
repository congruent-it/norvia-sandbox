using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.IntegrationConsole.Application.Agents;
using Norvia.IntegrationConsole.Application.Agents.Explain;

namespace Norvia.IntegrationConsole.Application.UnitTests.Agents;

public sealed class ExplainFailedRunAndDeadLetterHandlerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 30, 0, TimeSpan.Zero);

    private static readonly Suggestion Answer = new("Cause.", [new Evidence("A fact.", "r1", "list_failed_runs")], "Act.", Confidence.Medium);

    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    public ExplainFailedRunAndDeadLetterHandlerTests() => _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Answer);

    private AgentRequest Asked() => _agent.ReceivedCalls().Select(call => (AgentRequest)call.GetArguments()[0]!).Single();

    [Fact]
    public async Task ExplainFailedRun_AsksTheFixedQuestionAboutThatRun_OnTheTrailTheRunTracked()
    {
        var result = await new ExplainFailedRunHandler(_agent).Handle(new ExplainFailedRunQuery(" carrier-booking ", Start, " c-9 "), TestContext.Current.CancellationToken);

        result.Value.Should().Be(Answer);
        var request = Asked();
        request.AgentName.Should().Be("integration-operations");
        request.CorrelationId.Should().Be("c-9");
        request.Question.Should().Be(AgentQuestions.ExplainFailedRun("carrier-booking", Start));
    }

    [Fact]
    public async Task ExplainFailedRun_ARunThatTrackedNoTrail_GivesTheAgentsCallsATrailOfTheirOwn()
    {
        await new ExplainFailedRunHandler(_agent).Handle(new ExplainFailedRunQuery("billing", Start), TestContext.Current.CancellationToken);

        Guid.TryParse(Asked().CorrelationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExplainFailedRun_WithoutAFlow_IsInvalid_AndTheAgentIsNotAsked()
    {
        var result = await new ExplainFailedRunHandler(_agent).Handle(new ExplainFailedRunQuery(" ", Start), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExplainDeadLetter_AsksTheFixedQuestionAboutThatMessage_OnTheTrailTheMessageCarries()
    {
        var result = await new ExplainDeadLetterHandler(_agent).Handle(new ExplainDeadLetterQuery(" carrier-tracking ", 4, "c-1"), TestContext.Current.CancellationToken);

        result.Value.Should().Be(Answer);
        var request = Asked();
        request.CorrelationId.Should().Be("c-1");
        request.Question.Should().Be(AgentQuestions.ExplainDeadLetter("carrier-tracking", 4));
    }

    [Fact]
    public async Task ExplainDeadLetter_AMessageWithNoCorrelationId_GivesTheAgentsCallsATrailOfTheirOwn()
    {
        await new ExplainDeadLetterHandler(_agent).Handle(new ExplainDeadLetterQuery("carrier-tracking", 4), TestContext.Current.CancellationToken);

        Guid.TryParse(Asked().CorrelationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExplainDeadLetter_WithoutAQueue_IsInvalid_AndTheAgentIsNotAsked()
    {
        var result = await new ExplainDeadLetterHandler(_agent).Handle(new ExplainDeadLetterQuery("", 4), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Summarise_AsksTheFixedQuestion_NothingTheOperatorTyped()
    {
        var result = await new SummariseDeadLettersHandler(_agent).Handle(new SummariseDeadLettersQuery(), TestContext.Current.CancellationToken);

        result.Value.Should().Be(Answer);
        Asked().Question.Should().Be(AgentQuestions.WhatNeedsAttention);
    }

    [Fact]
    public async Task EachOfThem_TheAgentIsUnavailable_PassesTheFailureCodeOn()
    {
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));

        var run = await new ExplainFailedRunHandler(_agent).Handle(new ExplainFailedRunQuery("billing", Start), TestContext.Current.CancellationToken);
        var message = await new ExplainDeadLetterHandler(_agent).Handle(new ExplainDeadLetterQuery("q", 1), TestContext.Current.CancellationToken);
        var summary = await new SummariseDeadLettersHandler(_agent).Handle(new SummariseDeadLettersQuery(), TestContext.Current.CancellationToken);

        new[] { run, message, summary }.Should().OnlyContain(result => result.Status == ResultStatus.Unavailable && result.Errors.Single() == AgentFailureCodes.AgentUnavailable);
    }
}
