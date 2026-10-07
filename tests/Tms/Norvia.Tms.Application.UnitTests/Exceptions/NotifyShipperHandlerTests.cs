using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Exceptions.NotifyShipper;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class NotifyShipperHandlerTests
{
    private readonly IReadRepository<ExceptionCase> _exceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TransportOrder> _transportOrders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<TrackingEvent> _trackingEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IRepository<ShipperNotification> _notifications = Substitute.For<IRepository<ShipperNotification>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(3));
    private readonly NotifyShipperHandler _handler;

    public NotifyShipperHandlerTests()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        _shipments.SingleOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);
        _transportOrders.SingleOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _trackingEvents.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _handler = new NotifyShipperHandler(_exceptions, _shipments, _transportOrders, _trackingEvents, _notifications, _time);
    }

    [Fact]
    public async Task Handle_AnOpenException_RecordsARequestedNotification_RaisesItsEvent_AndSavesOnce()
    {
        var exception = OpenExceptions.DeliveryFailed();
        Found(exception);
        ShipperNotification? added = null;
        await _notifications.AddAsync(Arg.Do<ShipperNotification>(n => added = n), Arg.Any<CancellationToken>());

        var result = await _handler.Handle(Notify(), CancellationToken.None);

        added.Should().NotBeNull();
        result.Value.Should().Be(new NotifyShipperResult(added!.Id.ToString(), "EX-0000005", "SH-0000012", ShipperNotificationStatus.Requested, "corr-notify"));
        added.Status.Should().Be(ShipperNotificationStatus.Requested);
        added.Message.Should().Be("Delivery is delayed.");
        added.Planner.Should().Be("Anna de Vries");
        added.RequestedAt.Should().Be(Samples.Now.AddDays(3));
        added.CorrelationId.Should().Be(new CorrelationId("corr-notify"));
        added.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipperNotificationRequestedEvent>();
        await _notifications.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TwiceForTheSameException_RecordsTwoNotifications()
    {
        Found(OpenExceptions.DeliveryFailed());

        await _handler.Handle(Notify(), CancellationToken.None);
        await _handler.Handle(Notify(), CancellationToken.None);

        await _notifications.Received(2).AddAsync(Arg.Any<ShipperNotification>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("EX-0000099")]
    [InlineData("not-a-number")]
    public async Task Handle_AnUnknownException_IsRefused_WithExceptionNotFound(string number)
    {
        var result = await _handler.Handle(Notify(number), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotFound);
    }

    [Fact]
    public async Task Handle_AResolvedException_IsRefused_WithExceptionNotOpen()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "Done", new CorrelationId("corr-1"), Samples.Now.AddDays(2));
        Found(exception);

        var result = await _handler.Handle(Notify(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotOpen);
    }

    private async Task AssertRefused(Result<NotifyShipperResult> result, ExceptionRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code.ToString());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<ShipperNotification>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private void Found(ExceptionCase exception) =>
        _exceptions.SingleOrDefaultAsync(Arg.Is<ExceptionToResolveSpec>(s => s.Number == exception.Number), Arg.Any<CancellationToken>()).Returns(exception);

    private static NotifyShipperCommand Notify(string number = "EX-0000005") => new(number, "Delivery is delayed.", "Anna de Vries", "corr-notify");
}
