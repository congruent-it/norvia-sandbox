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

public sealed class ExplainNotYetBillableHandlerTests
{
    private readonly IReadRepository<TransportOrder> _repository = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    [Fact]
    public async Task Handle_ATransportOrder_AsksTheCustomerServiceAgentTheFixedQuestion_OnTheOrdersOwnTrail()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.RejectedOrder(correlationId: "corr-7"));
        var suggestion = new Suggestion("c", [new Evidence("e", "r1", "get_transport_order")], "a", Confidence.High);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(suggestion);

        var result = await new ExplainNotYetBillableHandler(_repository, _agent).Handle(new ExplainNotYetBillableQuery("TO-0000042"), CancellationToken.None);

        result.Value.Should().Be(suggestion);
        await _agent.Received(1).AskAsync(
            new AgentRequest("customer-service", CustomerServiceQuestions.ExplainNotYetBillable("TO-0000042"), "corr-7"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnavailableAgent_ReturnsItsFailureCode()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.RejectedOrder());
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentToolRefused));

        var result = await new ExplainNotYetBillableHandler(_repository, _agent).Handle(new ExplainNotYetBillableQuery("TO-0000042"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentToolRefused);
    }

    [Theory]
    [InlineData("TO-0000042")]
    [InlineData("not-a-number")]
    public async Task Handle_ANumberItDoesntFind_IsNotFound_AndAsksNobody(string number)
    {
        var result = await new ExplainNotYetBillableHandler(_repository, _agent).Handle(new ExplainNotYetBillableQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }
}
