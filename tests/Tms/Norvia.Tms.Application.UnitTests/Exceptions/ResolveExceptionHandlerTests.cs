using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Exceptions.Resolve;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.ProofsOfDelivery.ProofsOfDeliverySamples;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class ResolveExceptionHandlerTests
{
    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<TrackingEvent> _trackingEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IReadRepository<ExceptionCase> _openExceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(3));
    private readonly ResolveExceptionHandler _handler;

    public ResolveExceptionHandlerTests()
    {
        _handler = new ResolveExceptionHandler(_exceptions, _shipments, new BillingCheck(_orders, _shipments, _trackingEvents, _proofs, _openExceptions), _time);
    }

    [Fact]
    public async Task Handle_AnOpenException_ResolvesItWithTheOutcomeAndRemark_AndSavesOnce()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.ClearDomainEvents();
        Found(exception);

        var result = await _handler.Handle(Resolve(), CancellationToken.None);

        result.Value.Should().Be(new ResolveExceptionResult("EX-0000005", "SH-0000012", ExceptionStatus.Resolved, ExceptionOutcome.Redelivered, "corr-resolve"));
        exception.Outcome.Should().Be(ExceptionOutcome.Redelivered);
        exception.Remark.Should().Be("Redelivery Thursday");
        exception.ResolvedAt.Should().Be(Samples.Now.AddDays(3));
        exception.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionResolvedEvent>();
        await _exceptions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ExceptionOutcome.Redelivered)]
    [InlineData(ExceptionOutcome.NoActionNeeded)]
    public async Task Handle_TheLastOpenException_MakesADeliveredRatedOrderWithAProofOfDeliveryBillable_WhateverTheOutcome_InTheSameSave(ExceptionOutcome outcome)
    {
        var (exception, order) = DeliveredOrderBlockedBy(out var shipment);
        _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
            .Returns([new DeliveredEventRow(shipment.Id, Samples.Now.AddHours(3))]);
        _proofs.AnyAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(true);

        await _handler.Handle(Resolve() with { Outcome = outcome }, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.Billable);
        order.BillingCorrelationId.Should().Be(new CorrelationId("corr-resolve"));
        order.BecameBillableAt.Should().Be(Samples.Now.AddDays(3));
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TransportOrderBillableEvent>();
        await _openExceptions.Received().AnyAsync(Arg.Is<OpenExceptionOfShipmentSpec>(s => s != null), Arg.Any<CancellationToken>());
        await _exceptions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnotherExceptionStaysOpen_LeavesTheOrderNotYetBillable()
    {
        var (_, order) = DeliveredOrderBlockedBy(out var shipment);
        _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
            .Returns([new DeliveredEventRow(shipment.Id, Samples.Now.AddHours(3))]);
        _proofs.AnyAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(true);
        _openExceptions.AnyAsync(Arg.Any<OpenExceptionOfShipmentSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        await _handler.Handle(Resolve(), CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
    }

    [Fact]
    public async Task Handle_TheOrderHasNoProofOfDelivery_LeavesItNotYetBillable()
    {
        var (_, order) = DeliveredOrderBlockedBy(out var shipment);
        _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
            .Returns([new DeliveredEventRow(shipment.Id, Samples.Now.AddHours(3))]);

        await _handler.Handle(Resolve(), CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
    }

    [Theory]
    [InlineData("EX-0000099")]
    [InlineData("not-a-number")]
    public async Task Handle_AnUnknownException_IsRefused_WithExceptionNotFound(string number)
    {
        var result = await _handler.Handle(Resolve(number), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotFound);
    }

    [Fact]
    public async Task Handle_AResolvedException_IsRefused_WithExceptionNotOpen()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "Done", new CorrelationId("corr-1"), Samples.Now.AddDays(2));
        Found(exception);

        var result = await _handler.Handle(Resolve(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotOpen);
        exception.Outcome.Should().Be(ExceptionOutcome.NoActionNeeded);
    }

    private async Task AssertRefused(Result<ResolveExceptionResult> result, ExceptionRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code.ToString());
        await _exceptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private (ExceptionCase Exception, TransportOrder Order) DeliveredOrderBlockedBy(out Shipment shipment)
    {
        var delivered = ShipmentCarriedTo(ShipmentStatus.Delivered);
        shipment = delivered;
        var exception = OpenExceptions.DeliveryFailedOn(delivered);
        exception.ClearDomainEvents();
        Found(exception);
        var order = Samples.RatedOrder(sequence: 7);
        order.Plan(delivered.Number, CarriedBy.Carrier(VanDijk), new IdempotencyKey("plan"), new CorrelationId("corr-plan"), Samples.Now.AddHours(1));
        order.ClearDomainEvents();
        _orders.SingleOrDefaultAsync(Arg.Is<TransportOrderToBillSpec>(s => s.Number == order.Number), Arg.Any<CancellationToken>()).Returns(order);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentByNumberSpec>(s => s.Number == delivered.Number), Arg.Any<CancellationToken>()).Returns(delivered);
        return (exception, order);
    }

    private void Found(ExceptionCase exception) =>
        _exceptions.SingleOrDefaultAsync(Arg.Is<ExceptionToResolveSpec>(s => s.Number == exception.Number), Arg.Any<CancellationToken>()).Returns(exception);

    private static ResolveExceptionCommand Resolve(string number = "EX-0000005") =>
        new(number, ExceptionOutcome.Redelivered, "Redelivery Thursday", "corr-resolve");
}
