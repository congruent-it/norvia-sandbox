using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Explain;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Agents;

public sealed class ExplainRejectedBookingHandlerTests
{
    private readonly IReadRepository<TransportOrder> _repository = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    [Fact]
    public async Task Handle_ARejectedBooking_AsksTheCustomerServiceAgentTheFixedQuestion_OnTheBookingsOwnTrail()
    {
        var order = Samples.RejectedOrder(correlationId: "corr-2");
        _repository.FirstOrDefaultAsync(Arg.Is<RejectedBookingByIdSpec>(spec => spec.Id == order.Id), Arg.Any<CancellationToken>()).Returns(order);
        var suggestion = new Suggestion("c", [new Evidence("e", "r1", "get_rejected_booking")], "a", Confidence.High);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(suggestion);

        var result = await new ExplainRejectedBookingHandler(_repository, _agent).Handle(new ExplainRejectedBookingQuery(order.Id.Value), CancellationToken.None);

        result.Value.Should().Be(suggestion);
        await _agent.Received(1).AskAsync(
            new AgentRequest("customer-service", CustomerServiceQuestions.ExplainRejectedBooking(order.Id.Value), "corr-2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnavailableAgent_ReturnsItsFailureCode()
    {
        var order = Samples.RejectedOrder();
        _repository.FirstOrDefaultAsync(Arg.Any<RejectedBookingByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));

        var result = await new ExplainRejectedBookingHandler(_repository, _agent).Handle(new ExplainRejectedBookingQuery(order.Id.Value), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task Handle_AnIdItDoesntFind_IsNotFound_AndAsksNobody()
    {
        var result = await new ExplainRejectedBookingHandler(_repository, _agent).Handle(new ExplainRejectedBookingQuery(Guid.NewGuid()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }
}
