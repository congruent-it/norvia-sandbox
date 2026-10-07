using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The sample pairs in contracts/partner-api/samples/shipper-notifications: the event, and the notification the map makes of it.</summary>
public sealed class ShipperNotificationSampleTests
{
    private const string Folder = "PartnerApi/samples/shipper-notifications";
    private const string RequestedSuffix = ".shipper-notification-requested.json";

    public static TheoryData<string> Samples => new(
        Contracts.FileNames(Folder).Where(name => name.EndsWith(RequestedSuffix, StringComparison.Ordinal))
            .Select(name => name[..^RequestedSuffix.Length]));

    [Fact]
    public void Samples_CoverEveryExceptionCode()
    {
        var codes = Contracts.FileNames(Folder).Where(name => name.EndsWith(".shipper-notification.json", StringComparison.Ordinal))
            .Select(name => JsonNode.Parse(Contracts.Read($"{Folder}/{name}"))!["exceptionCode"]!.GetValue<string>());

        codes.Should().Contain(["COLLECTION_FAILED", "DELIVERY_FAILED", "DAMAGED", "RESERVATIONS", "LATE"]);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToShipperNotification_SampleEvent_GivesTheSampleNotification(string sample)
    {
        var requested = Contracts.Read($"{Folder}/{sample}{RequestedSuffix}");
        var expected = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.shipper-notification.json"));

        var notification = ShipperNotificationMap.ToShipperNotification(requested);

        JsonNode.DeepEquals(notification, expected).Should().BeTrue($"the map gave {notification?.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleNotification_KeepsToTheShipperNotificationSchema(string sample)
    {
        var results = Schemas.ShipperNotification.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.shipper-notification.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleEvent_KeepsToTheCanonicalSchema(string sample)
    {
        var results = Schemas.ShipperNotificationRequested.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}{RequestedSuffix}")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToShipperNotification_SampleEvent_KeepsToTheSchema_AndNamesNoCarrierOrShipment(string sample)
    {
        var notification = ShipperNotificationMap.ToShipperNotification(Contracts.Read($"{Folder}/{sample}{RequestedSuffix}"));

        var results = Schemas.ShipperNotification.Validate(notification);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
        notification!.ToJsonString().Should().NotContain("SH-00").And.NotContain("CAR-").And.NotContain("arrier");
    }

    [Fact]
    public void Validate_ANotificationNamingTheCarrierOrShipment_BreaksTheShipperNotificationSchema()
    {
        var notification = JsonNode.Parse(Contracts.Read($"{Folder}/delivery-failed-summer.shipper-notification.json"))!.AsObject();
        notification["shipmentNumber"] = "SH-0000041";

        Schemas.ShipperNotification.Validate(notification).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ANotificationWithAnEtaAndAWindow_BreaksTheShipperNotificationSchema()
    {
        var notification = JsonNode.Parse(Contracts.Read($"{Folder}/late-window-winter.shipper-notification.json"))!.AsObject();
        notification["eta"] = "16-01-2030 10:00";

        Schemas.ShipperNotification.Validate(notification).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ANotificationWithAnOffset_BreaksTheShipperNotificationSchema()
    {
        var notification = JsonNode.Parse(Contracts.Read($"{Folder}/delivery-failed-summer.shipper-notification.json"))!.AsObject();
        notification["eta"] = "2030-10-04T09:15:00+02:00";

        Schemas.ShipperNotification.Validate(notification).IsValid.Should().BeFalse();
    }
}
