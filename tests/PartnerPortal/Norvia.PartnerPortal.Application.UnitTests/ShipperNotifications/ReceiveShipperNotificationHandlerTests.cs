using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.ShipperNotifications.Receive;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Application.UnitTests.ShipperNotifications;

public sealed class ReceiveShipperNotificationHandlerTests
{
    private static readonly Guid NotificationId = Guid.Parse("0f1c0d9e-3b0a-4f43-a6f6-7f1f0d6a9b01");

    private static readonly ShipperNotificationContent Content = new(
        "BD-2030-0001", "TO-0000042", ShipperExceptionCode.DeliveryFailed, "Nobody was home.", null, "04-10-2030 09:00", "04-10-2030 12:00", "Eva Planner");

    private readonly IRepository<ShipperNotification> _repository = Substitute.For<IRepository<ShipperNotification>>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly ReceiveShipperNotificationHandler _handler;

    public ReceiveShipperNotificationHandlerTests()
    {
        _personas.All.Returns([Samples.BrouwerDranken, Samples.VanDijkTransport]);
        _handler = new ReceiveShipperNotificationHandler(_repository, _personas, new FakeTimeProvider(Samples.Now));
    }

    private static ReceiveShipperNotificationCommand Command(string partyId = "SHP-0001") => new(partyId, NotificationId, "corr-1", Content);

    [Fact]
    public async Task Handle_ANewNotification_PutsItInTheShippersInbox()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.Should().Be(new ReceiveShipperNotificationResult(NotificationId, Repeated: false));
        await _repository.Received(1).AddAsync(
            Arg.Is<ShipperNotification>(notification =>
                notification.PersonaId == Samples.BrouwerDranken.Id
                && notification.NotificationId == NotificationId
                && notification.CorrelationId == "corr-1"
                && notification.ReceivedAt == Samples.Now
                && notification.Content == Content),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameNotificationAgain_IsAcknowledged_WithoutBeingStoredTwice()
    {
        _repository.AnyAsync(Arg.Any<ShipperNotificationByNotificationIdSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ReceiveShipperNotificationResult(NotificationId, Repeated: true));
        await _repository.DidNotReceive().AddAsync(Arg.Any<ShipperNotification>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("SHP-9999")]
    [InlineData("CAR-0001")]
    public async Task Handle_ForNoShipperPersona_IsNotFound(string partyId)
    {
        var result = await _handler.Handle(Command(partyId), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _repository.DidNotReceive().AddAsync(Arg.Any<ShipperNotification>(), Arg.Any<CancellationToken>());
    }
}
