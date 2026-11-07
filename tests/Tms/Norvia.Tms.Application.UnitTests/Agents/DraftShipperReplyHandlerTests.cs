using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Reply;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Agents;

public sealed class DraftShipperReplyHandlerTests
{
    private readonly IReadRepository<TransportOrder> _repository = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();

    [Theory]
    [InlineData(ShipperQuestion.WhereIsMyOrder)]
    [InlineData(ShipperQuestion.HasItBeenDelivered)]
    [InlineData(ShipperQuestion.IsThereProofOfDelivery)]
    public async Task Handle_AsksTheReplyAgent_NotTheCustomerServiceAgent_TheFixedQuestion_OnTheOrdersOwnTrail(ShipperQuestion question)
    {
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.RejectedOrder(correlationId: "corr-7"));
        var draft = new Suggestion("Hello,", [new Evidence("e", "r1", "get_shipper_view")], "Edit it.", Confidence.High);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(draft);

        var result = await new DraftShipperReplyHandler(_repository, _agent).Handle(new DraftShipperReplyQuery("TO-0000042", question), CancellationToken.None);

        result.Value.Should().Be(draft);
        await _agent.Received(1).AskAsync(
            new AgentRequest("customer-service-reply", CustomerServiceQuestions.DraftReply("TO-0000042", question), "corr-7"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnavailableAgent_ReturnsItsFailureCode()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.RejectedOrder());
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));

        var result = await new DraftShipperReplyHandler(_repository, _agent).Handle(new DraftShipperReplyQuery("TO-0000042", ShipperQuestion.WhereIsMyOrder), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unavailable);
        result.Errors.Should().Equal(AgentFailureCodes.AgentUnavailable);
    }

    [Theory]
    [InlineData("TO-0000042", ShipperQuestion.WhereIsMyOrder, true)]
    [InlineData("not-a-number", ShipperQuestion.WhereIsMyOrder, false)]
    [InlineData("TO-0000042", (ShipperQuestion)99, false)]
    public async Task Handle_AnUnknownOrderOrQuestion_IsNotFound_AndAsksNobody(string number, ShipperQuestion question, bool orderMissing)
    {
        _repository.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(orderMissing ? null : Samples.RejectedOrder());

        var result = await new DraftShipperReplyHandler(_repository, _agent).Handle(new DraftShipperReplyQuery(number, question), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }
}
