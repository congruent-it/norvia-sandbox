using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Domain.UnitTests.ShipperNotifications;

public sealed class ShipperNotificationTemplatesTests
{
    [Theory]
    [InlineData(ExceptionType.CollectionFailed)]
    [InlineData(ExceptionType.DeliveryFailed)]
    [InlineData(ExceptionType.Damaged)]
    [InlineData(ExceptionType.Reservations)]
    [InlineData(ExceptionType.LateDelivery)]
    public void For_EachType_GivesItsOwnMessage_NamingTheShippersReference(ExceptionType type)
    {
        var message = ShipperNotificationTemplates.For(type, "PO-4711");

        message.Should().Contain("PO-4711");
        Enum.GetValues<ExceptionType>().Where(other => other != type)
            .Select(other => ShipperNotificationTemplates.For(other, "PO-4711")).Should().NotContain(message);
    }

    [Fact]
    public void For_WithoutAShipperReference_StillReads()
    {
        ShipperNotificationTemplates.For(ExceptionType.DeliveryFailed, null).Should().Contain("your shipment didn't succeed");
    }

    [Fact]
    public void For_AnUnknownType_Throws()
    {
        var act = () => ShipperNotificationTemplates.For((ExceptionType)99, null);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NotificationId_TryParse_AcceptsAGuid_AndRefusesOtherText()
    {
        var id = ShipperNotificationId.New();

        ShipperNotificationId.TryParse(id.ToString(), out var parsed).Should().BeTrue();
        parsed.Should().Be(id);
        ShipperNotificationId.TryParse("nope", out _).Should().BeFalse();
        ShipperNotificationId.TryParse(null, out _).Should().BeFalse();
    }
}
