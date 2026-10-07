using Norvia.Tms.Application.Exceptions.NotifyShipper;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.ShipperNotifications.Report;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.ShipperNotifications;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class PublishShipperNotificationEventsTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly ShipperNotification _notification = ShipperNotificationSamples.Requested(OpenExceptions.DeliveryFailed(), Samples.Now.AddDays(3));

    [Fact]
    public async Task Requested_PublishesShipperNotificationRequested_OnThePlannersTrail()
    {
        await new PublishShipperNotificationRequested(_outbox).Handle(new ShipperNotificationRequestedEvent(_notification), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ShipperNotificationRequestedV1>(e =>
            e.EventType == "ShipperNotificationRequested"
            && e.NotificationId == _notification.Id.ToString()
            && e.ExceptionNumber == "EX-0000005"
            && e.ShipmentNumber == "SH-0000012"
            && e.ShipperId == "SHP-0001"
            && e.ShipperReference == "SHIP-REF-1"
            && e.ExceptionType == "DeliveryFailed"
            && e.Message == "Your shipment is delayed."
            && e.Planner == "Anna de Vries"
            && e.CorrelationId == "corr-notify"
            && e.OccurredAt == Samples.Now.AddDays(3)));
    }

    [Fact]
    public void Requested_IsOnTheShipmentEventsTopic_TracedOnTheExceptionHandlingFlow()
    {
        var requested = ShipperNotificationRequestedV1.From(new ShipperNotificationRequestedEvent(_notification));

        requested.Topic.Should().Be("shipment-events");
        requested.EventVersion.Should().Be(1);
        requested.ApplicationProperties.Should()
            .Contain("flow", "exception-handling").And.Contain("partyId", "SHP-0001").And.Contain("shipmentNumber", "SH-0000012")
            .And.Contain("correlationId", "corr-notify");
    }

    [Fact]
    public async Task NotDelivered_PublishesShipperNotificationNotDelivered_WithTheCause_OnThePlannersTrail()
    {
        _notification.ReportNotDelivered(503, "Service unavailable.", Samples.Now.AddDays(4));

        await new PublishShipperNotificationNotDelivered(_outbox).Handle(new ShipperNotificationNotDeliveredEvent(_notification), CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ShipperNotificationNotDeliveredV1>(e =>
            e.EventType == "ShipperNotificationNotDelivered"
            && e.NotificationId == _notification.Id.ToString()
            && e.ExceptionNumber == "EX-0000005"
            && e.ShipperId == "SHP-0001"
            && e.StatusCode == 503
            && e.Message == "Service unavailable."
            && e.CorrelationId == "corr-notify"
            && e.OccurredAt == Samples.Now.AddDays(4)));
    }
}
