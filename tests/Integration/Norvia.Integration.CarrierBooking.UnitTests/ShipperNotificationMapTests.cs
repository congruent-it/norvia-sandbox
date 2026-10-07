using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public class ShipperNotificationMapTests
{
    private static JsonObject Requested(Action<JsonObject>? change = null)
    {
        var requested = JsonNode.Parse(Contracts.Read("PartnerApi/samples/shipper-notifications/delivery-failed-summer.shipper-notification-requested.json"))!.AsObject();
        change?.Invoke(requested);
        return requested;
    }

    [Theory]
    [InlineData("CollectionFailed", "COLLECTION_FAILED")]
    [InlineData("DeliveryFailed", "DELIVERY_FAILED")]
    [InlineData("Damaged", "DAMAGED")]
    [InlineData("Reservations", "RESERVATIONS")]
    [InlineData("LateDelivery", "LATE")]
    public void ToShipperNotification_AnExceptionType_GivesTheShipperCode(string type, string code)
    {
        var notification = ShipperNotificationMap.ToShipperNotification(Requested(r => r["exceptionType"] = type).ToJsonString());

        notification!["exceptionCode"]!.GetValue<string>().Should().Be(code);
    }

    [Fact]
    public void ToShipperNotification_AnUnknownExceptionType_GivesNothing() =>
        ShipperNotificationMap.ToShipperNotification(Requested(r => r["exceptionType"] = "Other").ToJsonString()).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void ToShipperNotification_TextThatIsNoEvent_GivesNothing(string text) =>
        ShipperNotificationMap.ToShipperNotification(text).Should().BeNull();

    [Fact]
    public void ToShipperNotification_AnEventWithoutADeliveryCountry_LeavesTheTimesOut()
    {
        var notification = ShipperNotificationMap.ToShipperNotification(Requested(r => r["deliveryCountry"] = null).ToJsonString());

        notification!.ContainsKey("eta").Should().BeFalse();
    }

    [Fact]
    public void ToShipperNotification_AnAgreedWindow_ReplacesTheEta()
    {
        var notification = ShipperNotificationMap.ToShipperNotification(Requested(r =>
        {
            r["agreedDeliveryWindowEarliest"] = "2030-10-04T06:00:00+00:00";
            r["agreedDeliveryWindowLatest"] = "2030-10-04T12:00:00+00:00";
        }).ToJsonString());

        notification!.ContainsKey("eta").Should().BeFalse();
        notification["deliverFrom"]!.GetValue<string>().Should().Be("04-10-2030 08:00");
        notification["deliverUntil"]!.GetValue<string>().Should().Be("04-10-2030 14:00");
    }

    [Theory]
    [InlineData("notificationId")]
    [InlineData("shipperReference")]
    [InlineData("transportOrderNumber")]
    [InlineData("message")]
    [InlineData("planner")]
    public void ToShipperNotification_AnEventWithoutARequiredValue_GivesNothing(string name) =>
        ShipperNotificationMap.ToShipperNotification(Requested(r => r[name] = null).ToJsonString()).Should().BeNull();
}
