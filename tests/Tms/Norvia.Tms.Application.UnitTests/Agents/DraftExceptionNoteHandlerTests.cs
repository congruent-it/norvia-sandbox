using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Agents;

public sealed class DraftExceptionNoteHandlerTests
{
    private readonly IReadRepository<ExceptionCase> _repository = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    private DraftExceptionNoteHandler Handler => new(_repository, _agent);

    [Fact]
    public async Task Handle_AnOpenException_AsksTheNoteAgent_TheFixedQuestion_OnTheExceptionsOwnTrail()
    {
        var exception = Exceptions.OpenExceptions.DeliveryFailed();
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(exception);
        var suggestion = new Suggestion("A draft.", [new Evidence("e", "r1", "get_exception_timeline")], "Edit it.", Confidence.Medium);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(suggestion);

        var result = await Handler.Handle(new DraftExceptionNoteQuery("EX-0000005"), CancellationToken.None);

        result.Value.Should().Be(suggestion);
        await _agent.Received(1).AskAsync(
            new AgentRequest("planner-exception-note", PlannerQuestions.DraftExceptionNote("EX-0000005"), exception.CorrelationId.Value), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AResolvedException_IsNotFound_AndAsksNobody()
    {
        var exception = Exceptions.OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.ClaimHandledOutsideTms, "Claim filed", new CorrelationId("corr-resolve"), Samples.Now.AddDays(2));
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(exception);

        var result = await Handler.Handle(new DraftExceptionNoteQuery("EX-0000005"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("EX-0000005", true)]
    [InlineData("not-a-number", false)]
    public async Task Handle_AnUnknownException_IsNotFound_AndAsksNobody(string number, bool missing)
    {
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(missing ? null : Exceptions.OpenExceptions.DeliveryFailed());

        var result = await Handler.Handle(new DraftExceptionNoteQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_AnUnavailableAgent_ReturnsItsFailureCode()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<ExceptionByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Exceptions.OpenExceptions.DeliveryFailed());
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));

        var result = await Handler.Handle(new DraftExceptionNoteQuery("EX-0000005"), CancellationToken.None);

        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }
}
