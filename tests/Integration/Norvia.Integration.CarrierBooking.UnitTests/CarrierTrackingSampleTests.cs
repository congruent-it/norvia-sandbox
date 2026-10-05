using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The sample pairs in contracts/partner-api/samples/carrier-tracking: a Carrier's tracking message, and the Canonical Model Tracking event the map makes of it for Carrier CAR-0001.</summary>
public sealed class CarrierTrackingSampleTests
{
    private const string Folder = "PartnerApi/samples/carrier-tracking";
    private const string CarrierId = "CAR-0001";

    public static TheoryData<string> Samples => new(
        Contracts.FileNames(Folder).Where(name => name.EndsWith(".carrier-tracking.json", StringComparison.Ordinal))
            .Select(name => name[..^".carrier-tracking.json".Length]));

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToTrackingEvent_SampleMessage_GivesTheSampleTrackingEvent(string sample)
    {
        var expected = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.tracking-event.json"));

        var tracking = CarrierTrackingMap.ToTrackingEvent(Contracts.Read($"{Folder}/{sample}.carrier-tracking.json"), CarrierId);

        JsonNode.DeepEquals(tracking, expected).Should().BeTrue($"the map gave {tracking.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleMessage_KeepsToTheCarrierTrackingSchema(string sample)
    {
        var results = Schemas.CarrierTracking.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-tracking.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToTrackingEvent_SampleMessage_KeepsToTheCanonicalSchema(string sample)
    {
        var tracking = CarrierTrackingMap.ToTrackingEvent(Contracts.Read($"{Folder}/{sample}.carrier-tracking.json"), CarrierId);

        var results = Schemas.TrackingEvent.Validate(tracking);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [InlineData("bookingRef")]
    [InlineData("eventId")]
    [InlineData("statusCode")]
    [InlineData("eventTime")]
    [InlineData("place")]
    [InlineData("postcode")]
    [InlineData("country")]
    public void Validate_AMessageWithoutARequiredField_BreaksTheCarrierTrackingSchema(string field)
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/collected.carrier-tracking.json"))!.AsObject();
        message.Remove(field);

        Schemas.CarrierTracking.Validate(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_AMessageNamingTheCarrier_BreaksTheCarrierTrackingSchema()
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/collected.carrier-tracking.json"))!.AsObject();
        message["carrierId"] = "CAR-0002";

        Schemas.CarrierTracking.Validate(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ACodeNotInTheSharedList_BreaksTheCarrierTrackingSchema()
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/collected.carrier-tracking.json"))!.AsObject();
        message["statusCode"] = "DLV";

        Schemas.CarrierTracking.Validate(message).IsValid.Should().BeFalse();
    }
}
