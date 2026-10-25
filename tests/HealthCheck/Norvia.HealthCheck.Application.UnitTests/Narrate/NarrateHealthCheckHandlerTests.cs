using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;
using Norvia.HealthCheck.Application.Narrate;

namespace Norvia.HealthCheck.Application.UnitTests.Narrate;

public sealed class NarrateHealthCheckHandlerTests
{
    private const string CorrelationId = "5b0d7f8e2c414a3e9d6a0f1e2d3c4b5a";

    private static readonly Narration Answer = new("One problem.", [new NarratedItem("F-DLQ-ALERT", "Nobody is told.", "r1", "run_checks")]);

    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    [Fact]
    public async Task Handle_AsksTheEstateHealthCheckAgent_TheFixedQuestion_AboutThisRun()
    {
        _agent.NarrateAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Answer);

        var result = await new NarrateHealthCheckHandler(_agent).Handle(new NarrateHealthCheckQuery($" {CorrelationId} "), TestContext.Current.CancellationToken);

        result.Value.Should().Be(Answer);
        await _agent.Received(1).NarrateAsync(
            new AgentRequest("estate-health-check", HealthCheckQuestions.Narrate, CorrelationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheAgentIsUnavailable_PassesTheFailureCodeOn()
    {
        _agent.NarrateAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Narration>.Unavailable(AgentFailureCodes.AgentToolRefused));

        var result = await new NarrateHealthCheckHandler(_agent).Handle(new NarrateHealthCheckQuery(CorrelationId), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WithoutACorrelationId_IsInvalid_AndAsksNobody(string? correlationId)
    {
        var result = await new NarrateHealthCheckHandler(_agent).Handle(new NarrateHealthCheckQuery(correlationId!), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _agent.DidNotReceiveWithAnyArgs().NarrateAsync(default!, TestContext.Current.CancellationToken);
    }
}
