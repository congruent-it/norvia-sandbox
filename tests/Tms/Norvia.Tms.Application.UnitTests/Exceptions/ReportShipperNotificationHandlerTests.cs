using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.ShipperNotifications.Report;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class ReportShipperNotificationHandlerTests
{
    private readonly IRepository<ShipperNotification> _notifications = Substitute.For<IRepository<ShipperNotification>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(4));
    private readonly ShipperNotification _notification = ShipperNotificationSamples.Requested(OpenExceptions.DeliveryFailed(), Samples.Now.AddDays(3));
    private readonly ReportShipperNotificationSentHandler _sent;
    private readonly ReportShipperNotificationNotDeliveredHandler _notDelivered;

    public ReportShipperNotificationHandlerTests()
    {
        _notification.ClearDomainEvents();
        _notifications.SingleOrDefaultAsync(Arg.Is<ShipperNotificationByIdSpec>(_ => true), Arg.Any<CancellationToken>()).Returns(_notification);
        _sent = new ReportShipperNotificationSentHandler(_notifications, _time);
        _notDelivered = new ReportShipperNotificationNotDeliveredHandler(_notifications, _time);
    }

    [Fact]
    public async Task Sent_ARequestedNotification_MovesItToSent_RaisesNothing_AndSaves()
    {
        var result = await _sent.Handle(new ReportShipperNotificationSentCommand(_notification.Id.ToString(), "corr-notify"), CancellationToken.None);

        result.Value.Should().Be(new ShipperNotificationReportResult(_notification.Id.ToString(), ShipperNotificationStatus.Sent));
        _notification.SentAt.Should().Be(Samples.Now.AddDays(4));
        _notification.DomainEvents.Should().BeEmpty();
        await _notifications.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotDelivered_ARequestedNotification_RecordsTheCause_AndRaisesShipperNotificationNotDelivered()
    {
        var result = await _notDelivered.Handle(
            new ReportShipperNotificationNotDeliveredCommand(_notification.Id.ToString(), "corr-notify", 503, "Service unavailable."), CancellationToken.None);

        result.Value.Should().Be(new ShipperNotificationReportResult(_notification.Id.ToString(), ShipperNotificationStatus.NotDelivered));
        _notification.NotDeliveredStatusCode.Should().Be(503);
        _notification.NotDeliveredMessage.Should().Be("Service unavailable.");
        _notification.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipperNotificationNotDeliveredEvent>();
        await _notifications.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALateOrRepeatReport_IsIgnored_ChangesNothing_AndRaisesNothing()
    {
        await _sent.Handle(new ReportShipperNotificationSentCommand(_notification.Id.ToString(), "corr-notify"), CancellationToken.None);
        _notifications.ClearReceivedCalls();

        var repeat = await _sent.Handle(new ReportShipperNotificationSentCommand(_notification.Id.ToString(), "corr-notify"), CancellationToken.None);
        var late = await _notDelivered.Handle(
            new ReportShipperNotificationNotDeliveredCommand(_notification.Id.ToString(), "corr-notify", 503, "Down"), CancellationToken.None);

        repeat.Value.Should().Be(new ShipperNotificationReportResult(_notification.Id.ToString(), ShipperNotificationStatus.Sent, Ignored: true));
        late.Value.Ignored.Should().BeTrue();
        _notification.Status.Should().Be(ShipperNotificationStatus.Sent);
        _notification.DomainEvents.Should().BeEmpty();
        await _notifications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public async Task AnUnknownNotification_IsRefused_WithShipperNotificationNotFound(string id)
    {
        _notifications.SingleOrDefaultAsync(Arg.Any<ShipperNotificationByIdSpec>(), Arg.Any<CancellationToken>()).Returns((ShipperNotification?)null);

        var result = await _sent.Handle(new ReportShipperNotificationSentCommand(id, "corr-notify"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(ExceptionRefusalCode.ShipperNotificationNotFound));
        await _notifications.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
