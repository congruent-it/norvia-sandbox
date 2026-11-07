using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Agents;
using Norvia.Tms.Application.Agents.Explain;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Billing.GetReleaseEligibility;
using Norvia.Tms.Application.Billing.Release;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.ProofsOfDelivery.ProofsOfDeliverySamples;

namespace Norvia.Tms.Application.UnitTests.Agents;

public sealed class RecommendReleaseHandlerTests
{
    private static readonly DateTimeOffset DeliveredAt = Samples.Now.AddDays(2);

    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _trackingEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IReadRepository<ExceptionCase> _exceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly IAgentClient _agent = Substitute.For<IAgentClient>();
    private readonly ReleaseEligibilityCheck _eligibility;

    public RecommendReleaseHandlerTests()
    {
        _eligibility = new ReleaseEligibilityCheck(new BillingCheck(_orders, _shipments, _trackingEvents, _proofs, _exceptions), _proofs, _exceptions);
    }

    private static Suggestion Suggests(SuggestedActionKind kind) =>
        new("c", [new Evidence("e", "r1", "get_release_eligibility")], "a", Confidence.Medium, kind, new AgentRun("customer-service", "run-7"));

    [Fact]
    public async Task Handle_AnEligibleOrder_AsksTheFixedQuestion_AndPassesAReleaseSuggestionOn()
    {
        var order = OrderDelivered();
        var suggestion = Suggests(SuggestedActionKind.Release);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(suggestion);

        var result = await new RecommendReleaseHandler(_orders, _eligibility, _agent).Handle(new RecommendReleaseQuery(order.Number!.Value), CancellationToken.None);

        result.Value.Should().Be(suggestion);
        await _agent.Received(1).AskAsync(
            new AgentRequest("customer-service", CustomerServiceQuestions.RecommendRelease(order.Number.Value), order.CorrelationId.Value),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SuggestedActionKind.Wait)]
    [InlineData(SuggestedActionKind.NeedsAPerson)]
    public async Task Handle_AWaitOrNeedsAPersonSuggestion_IsPassedOnAsItIs(SuggestedActionKind kind)
    {
        var order = OrderDelivered();
        var suggestion = Suggests(kind);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(suggestion);

        var result = await new RecommendReleaseHandler(_orders, _eligibility, _agent).Handle(new RecommendReleaseQuery(order.Number!.Value), CancellationToken.None);

        result.Value.Should().Be(suggestion);
    }

    [Fact]
    public async Task Handle_AReleaseSuggestionForAnOrderWithAnOpenException_BecomesNeedsAPerson_WithTheBlockingReason()
    {
        var order = OrderDelivered();
        _exceptions.AnyAsync(Arg.Any<OpenExceptionOfShipmentSpec>(), Arg.Any<CancellationToken>()).Returns(true);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Suggests(SuggestedActionKind.Release));

        var result = await new RecommendReleaseHandler(_orders, _eligibility, _agent).Handle(new RecommendReleaseQuery(order.Number!.Value), CancellationToken.None);

        result.Value.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        result.Value.SuggestedAction.Should().Contain("OpenException");
    }

    [Fact]
    public async Task Handle_AReleaseSuggestionForAnOrderThatIsNotDelivered_BecomesNeedsAPerson()
    {
        var order = OrderDelivered(delivered: false);
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Suggests(SuggestedActionKind.Release));

        var result = await new RecommendReleaseHandler(_orders, _eligibility, _agent).Handle(new RecommendReleaseQuery(order.Number!.Value), CancellationToken.None);

        result.Value.Action.Should().Be(SuggestedActionKind.NeedsAPerson);
        result.Value.SuggestedAction.Should().Contain("ShipmentNotDelivered");
    }

    [Fact]
    public async Task Handle_AnUnavailableAgent_ReturnsItsFailureCode()
    {
        var order = OrderDelivered();
        _agent.AskAsync(Arg.Any<AgentRequest>(), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));

        var result = await new RecommendReleaseHandler(_orders, _eligibility, _agent).Handle(new RecommendReleaseQuery(order.Number!.Value), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unavailable);
    }

    [Theory]
    [InlineData("TO-0000099")]
    [InlineData("not-a-number")]
    public async Task Handle_ANumberItDoesntFind_IsNotFound_AndAsksNobody(string number)
    {
        var result = await new RecommendReleaseHandler(_orders, _eligibility, _agent).Handle(new RecommendReleaseQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _agent.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
    }

    private TransportOrder OrderDelivered(bool delivered = true)
    {
        var order = Samples.RatedOrder(sequence: 7);
        var shipment = ShipmentCarriedTo(delivered ? ShipmentStatus.Delivered : ShipmentStatus.Booked);
        order.Plan(shipment.Number, Norvia.Tms.Domain.Shipments.CarriedBy.Carrier(VanDijk), new IdempotencyKey("plan"), new CorrelationId("plan-corr"), Samples.Now.AddHours(1));
        order.ClearDomainEvents();
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _shipments.SingleOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);
        if (delivered)
        {
            _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
                .Returns([new DeliveredEventRow(shipment.Id, DeliveredAt)]);
        }

        return order;
    }
}

public sealed class GetReleaseEligibilityHandlerTests
{
    private static readonly DateTimeOffset DeliveredAt = Samples.Now.AddDays(2);

    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _trackingEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IReadRepository<ExceptionCase> _exceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly GetReleaseEligibilityHandler _handler;

    public GetReleaseEligibilityHandlerTests()
    {
        _handler = new GetReleaseEligibilityHandler(
            _orders, new ReleaseEligibilityCheck(new BillingCheck(_orders, _shipments, _trackingEvents, _proofs, _exceptions), _proofs, _exceptions));
    }

    [Fact]
    public async Task Handle_ARatedDeliveredOrderWithoutAProof_IsEligible()
    {
        var order = OrderAndShipment(delivered: true);

        var result = await _handler.Handle(new GetReleaseEligibilityQuery(order.Number!.Value), CancellationToken.None);

        result.Value.Should().BeEquivalentTo(new ReleaseEligibility(order.Number.Value, true, order.ShipmentNumber!.Value, DeliveredAt, []));
    }

    [Fact]
    public async Task Handle_GivesTheSameAnswerAsTheReleaseCommand_ForEveryRefusalOfTheRule()
    {
        var order = OrderAndShipment(delivered: true);
        order.ReleaseWithoutProofOfDelivery(DeliveredAt, "CMR by post", "Jan", Samples.Now.AddDays(3));
        var refusal = order.ReleaseRefusal(DeliveredAt)!;

        var result = await _handler.Handle(new GetReleaseEligibilityQuery(order.Number!.Value), CancellationToken.None);

        result.Value.Eligible.Should().BeFalse();
        result.Value.BlockingReasons.Should().Contain(refusal.Code.ToString());
    }

    [Fact]
    public async Task Handle_AnOrderNotDelivered_IsNotEligible_AndSaysSo()
    {
        var order = OrderAndShipment(delivered: false);

        var result = await _handler.Handle(new GetReleaseEligibilityQuery(order.Number!.Value), CancellationToken.None);

        result.Value.Should().BeEquivalentTo(new ReleaseEligibility(order.Number.Value, false, order.ShipmentNumber!.Value, null, ["ShipmentNotDelivered"]));
    }

    [Fact]
    public async Task Handle_AnOrderWithAnOpenException_IsNotEligible_BecauseTheReleaseWouldNotMakeItBillable()
    {
        var order = OrderAndShipment(delivered: true);
        _exceptions.AnyAsync(Arg.Any<OpenExceptionOfShipmentSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(new GetReleaseEligibilityQuery(order.Number!.Value), CancellationToken.None);

        result.Value.Eligible.Should().BeFalse();
        result.Value.BlockingReasons.Should().Equal("OpenException");
    }

    [Fact]
    public async Task Handle_AnOrderWithAProofOfDelivery_IsNotEligible_BecauseThereIsNothingToRelease()
    {
        var order = OrderAndShipment(delivered: true);
        _proofs.AnyAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(new GetReleaseEligibilityQuery(order.Number!.Value), CancellationToken.None);

        result.Value.BlockingReasons.Should().Equal("ProofOfDeliveryReceived");
    }

    [Fact]
    public async Task Handle_AnUnratedOrder_IsNotEligible()
    {
        var order = OrderAndShipment(delivered: true, rated: false);

        var result = await _handler.Handle(new GetReleaseEligibilityQuery(order.Number!.Value), CancellationToken.None);

        result.Value.BlockingReasons.Should().Equal("NotRated");
    }

    [Theory]
    [InlineData("TO-0000099")]
    [InlineData("not-a-number")]
    public async Task Handle_ANumberItDoesntFind_IsNotFound(string number)
    {
        var result = await _handler.Handle(new GetReleaseEligibilityQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    private TransportOrder OrderAndShipment(bool delivered, bool rated = true)
    {
        var order = rated ? Samples.RatedOrder(sequence: 7) : Samples.RatingFailedOrder(sequence: 7);
        var shipment = ShipmentCarriedTo(delivered ? ShipmentStatus.Delivered : ShipmentStatus.Booked);
        order.Plan(shipment.Number, Norvia.Tms.Domain.Shipments.CarriedBy.Carrier(VanDijk), new IdempotencyKey("plan"), new CorrelationId("plan-corr"), Samples.Now.AddHours(1));
        order.ClearDomainEvents();
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _shipments.SingleOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);
        if (delivered)
        {
            _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
                .Returns([new DeliveredEventRow(shipment.Id, DeliveredAt)]);
        }

        return order;
    }
}
