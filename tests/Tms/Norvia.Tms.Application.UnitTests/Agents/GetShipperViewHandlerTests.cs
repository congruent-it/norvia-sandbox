using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.Agents.Reply;
using Norvia.Tms.Application.UnitTests.Exceptions;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Agents;

/// <summary>The reply agent's one tool reads what the Shipper sees of an order and the notifications that reached the Shipper (#426, ADR 0011).</summary>
public sealed class GetShipperViewHandlerTests
{
    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _events = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IReadRepository<ShipperNotification> _notifications = Substitute.For<IReadRepository<ShipperNotification>>();

    private GetShipperViewHandler Handler() => new(_orders, _shipments, _events, _proofs, _notifications);

    public GetShipperViewHandlerTests()
    {
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _notifications.ListAsync(Arg.Any<SentShipperNotificationsOfTransportOrderSpec>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task Handle_AnOrderWithNoShipment_IsTheShipperTrackingReadModel_Received_WithNoNotifications()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.AcceptedOrder(sequence: 7));

        var result = await Handler().Handle(new GetShipperViewQuery("TO-0000007"), CancellationToken.None);

        result.Value.Tracking.TransportOrderNumber.Should().Be("TO-0000007");
        result.Value.Tracking.TrackingStatus.Should().Be(TrackingStatus.Received);
        result.Value.Tracking.ProofOfDelivery.Should().BeNull();
        result.Value.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NotificationsThatWereSent_AreTheirTimeAndTheTextTheShipperReceived()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.AcceptedOrder(sequence: 7));
        var sent = ShipperNotificationSamples.Requested(OpenExceptions.DeliveryFailed(), Samples.Now);
        sent.ReportSent(Samples.Now.AddHours(1));
        var requestedOnly = ShipperNotificationSamples.Requested(OpenExceptions.DeliveryFailed(), Samples.Now);
        _notifications.ListAsync(Arg.Any<SentShipperNotificationsOfTransportOrderSpec>(), Arg.Any<CancellationToken>()).Returns([sent, requestedOnly]);

        var result = await Handler().Handle(new GetShipperViewQuery("TO-0000007"), CancellationToken.None);

        result.Value.Notifications.Should().Equal(new ShipperNotificationSent(Samples.Now.AddHours(1), "Your shipment is delayed."));
    }

    [Theory]
    [InlineData("TO-0000042")]
    [InlineData("not-a-number")]
    public async Task Handle_ANumberItDoesntFind_IsNotFound(string number)
    {
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns((TransportOrder?)null);

        var result = await Handler().Handle(new GetShipperViewQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
