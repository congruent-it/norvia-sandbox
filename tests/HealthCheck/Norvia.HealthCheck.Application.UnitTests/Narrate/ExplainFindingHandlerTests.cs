using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Application.Narrate;

namespace Norvia.HealthCheck.Application.UnitTests.Narrate;

public sealed class ExplainFindingHandlerTests
{
    private const string CorrelationId = "5b0d7f8e2c414a3e9d6a0f1e2d3c4b5a";

    private static readonly NarratedItem Item = new("F-DLQ-ALERT", "Nobody is told.", "r1", "run_checks");

    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    private Task<Result<NarratedItem>> Ask(string findingId = "F-DLQ-ALERT", FindingQuestion question = FindingQuestion.WhyIsThisAProblem, string correlationId = CorrelationId) =>
        new ExplainFindingHandler(_agent).Handle(new ExplainFindingQuery(correlationId, findingId, question), TestContext.Current.CancellationToken).AsTask();

    [Theory]
    [InlineData(FindingQuestion.WhyIsThisAProblem)]
    [InlineData(FindingQuestion.WhatChangesIfIFixIt)]
    public async Task Handle_AsksTheFixedQuestion_AboutThatFindingOnly(FindingQuestion question)
    {
        _agent.NarrateAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(new Narration("One.", [Item]));

        var result = await Ask(" F-DLQ-ALERT ", question);

        result.Value.Should().Be(Item);
        var expected = question == FindingQuestion.WhyIsThisAProblem ? HealthCheckQuestions.Why("F-DLQ-ALERT") : HealthCheckQuestions.WhatChanges("F-DLQ-ALERT");
        await _agent.Received(1).NarrateAsync(new AgentRequest("estate-health-check", expected, CorrelationId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheAgentIsUnavailable_PassesTheFailureCodeOn()
    {
        _agent.NarrateAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Narration>.Unavailable(AgentFailureCodes.AgentToolRefused));

        var result = await Ask();

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Fact]
    public async Task Handle_WhenTheAgentExplainsAnotherFindingOrMore_RefusesTheAnswer()
    {
        _agent.NarrateAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(
            new Narration("Two.", [Item, Item with { Id = "F-OTHER" }]),
            new Narration("Other.", [Item with { Id = "F-OTHER" }]),
            new Narration("None.", []));

        (await Ask()).Status.Should().Be(ResultStatus.Unavailable);
        (await Ask()).Status.Should().Be(ResultStatus.Unavailable);
        (await Ask()).Status.Should().Be(ResultStatus.Unavailable);
    }

    [Theory]
    [InlineData("", "F-DLQ-ALERT", FindingQuestion.WhyIsThisAProblem)]
    [InlineData(CorrelationId, "  ", FindingQuestion.WhyIsThisAProblem)]
    [InlineData(CorrelationId, "F-DLQ-ALERT", (FindingQuestion)99)]
    public async Task Handle_WithoutARunAFindingOrAKnownQuestion_IsInvalid_AndAsksNobody(string correlationId, string findingId, FindingQuestion question)
    {
        var result = await Ask(findingId, question, correlationId);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _agent.DidNotReceiveWithAnyArgs().NarrateAsync(default!, TestContext.Current.CancellationToken);
    }
}
