using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Application.ShipperNotifications.List;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Application.UnitTests.ShipperNotifications;

public sealed class ListShipperNotificationsHandlerTests
{
    [Fact]
    public async Task Handle_ReadsThePersonasInbox_WithTheTransportOrderAndTheShipperReference()
    {
        var content = new ShipperNotificationContent(
            "BD-2030-0001", "TO-0000042", ShipperExceptionCode.Damaged, "A pallet was damaged.", DeliveryTiming.WithEta(new DateTime(2030, 10, 3, 14, 30, 0)), "Eva Planner");
        var notification = ShipperNotification.Receive(Samples.BrouwerDranken.Id, Guid.NewGuid(), "corr-1", Samples.Now, content);
        var repository = Substitute.For<IReadRepository<ShipperNotification>>();
        repository.ListAsync(Arg.Any<ShipperNotificationsOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([notification]);

        var inbox = await new ListShipperNotificationsHandler(repository).Handle(new ListShipperNotificationsQuery("brouwer-dranken"), CancellationToken.None);

        var view = inbox.Should().ContainSingle().Subject;
        view.NotificationId.Should().Be(notification.NotificationId);
        view.ReceivedAt.Should().Be(Samples.Now);
        view.TransportOrderNumber.Should().Be("TO-0000042");
        view.ShipperReference.Should().Be("BD-2030-0001");
        view.ExceptionCode.Should().Be(ShipperExceptionCode.Damaged);
        view.Message.Should().Be("A pallet was damaged.");
        view.Timing.Eta.Should().Be(new DateTime(2030, 10, 3, 14, 30, 0));
        view.Contact.Should().Be("Eva Planner");
        view.CorrelationId.Should().Be("corr-1");
    }
}
