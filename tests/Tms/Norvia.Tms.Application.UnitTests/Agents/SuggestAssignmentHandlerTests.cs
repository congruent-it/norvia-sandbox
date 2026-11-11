using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Agents;

public sealed class SuggestAssignmentHandlerTests
{
    private readonly IReadRepository<TransportOrder> _repository = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    [Fact]
    public async Task Handle_AsksThePlannerAgent_TheFixedQuestion_OnTheOrdersOwnTrail()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.AcceptedOrder(sequence: 42, correlationId: "corr-7"));
        var suggestion = new Suggestion("Own fleet fits.", [new Evidence("e", "r1", "get_assignment_options")], "Assign to Own fleet.", Confidence.High);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(suggestion);

        var result = await new SuggestAssignmentHandler(_repository, _agent).Handle(new SuggestAssignmentQuery("TO-0000042"), CancellationToken.None);

        result.Value.Should().Be(suggestion);
        await _agent.Received(1).AskAsync(new AgentRequest("planner", PlannerQuestions.SuggestAssignment("TO-0000042"), "corr-7"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithATally_AsksTheQuestionThatNamesTheOrdersAlreadyAssumed()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.AcceptedOrder(sequence: 42, correlationId: "corr-7"));
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(new Suggestion("c", [], "a", Confidence.High));

        await new SuggestAssignmentHandler(_repository, _agent).Handle(new SuggestAssignmentQuery("TO-0000042", ["TO-0000040"]), CancellationToken.None);

        await _agent.Received(1).AskAsync(
            new AgentRequest("planner", PlannerQuestions.SuggestAssignment("TO-0000042", ["TO-0000040"]), "corr-7"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnavailableAgent_ReturnsItsFailureCode()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.AcceptedOrder(sequence: 42));
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));

        var result = await new SuggestAssignmentHandler(_repository, _agent).Handle(new SuggestAssignmentQuery("TO-0000042"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Theory]
    [InlineData("TO-0000042", true)]
    [InlineData("not-a-number", false)]
    public async Task Handle_AnUnknownOrder_IsNotFound_AndAsksNobody(string number, bool orderMissing)
    {
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(orderMissing ? null : Samples.AcceptedOrder());

        var result = await new SuggestAssignmentHandler(_repository, _agent).Handle(new SuggestAssignmentQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }
}
