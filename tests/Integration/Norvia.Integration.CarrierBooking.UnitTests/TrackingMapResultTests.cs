using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class TrackingMapResultTests
{
    private const string Collected = """{"bookingRef":"B-1","eventId":"E-1","statusCode":"PU","eventTime":"2030-10-01T08:00:00","place":"Rotterdam","postcode":"3011AA","country":"NLD"}""";

    [Fact]
    public void Map_AKnownStatusCode_ReturnsTheCanonicalEvent()
    {
        var result = CarrierTrackingMap.Map(Collected, "CAR-0001");

        result.IsMapped.Should().BeTrue();
        result.TrackingEvent!["type"]!.GetValue<string>().Should().Be("Collected");
        result.UnknownStatusCode.Should().BeNull();
    }

    [Fact]
    public void Map_AnUnknownStatusCode_ReturnsItWithoutThrowing()
    {
        var message = JsonNode.Parse(Collected)!.AsObject();
        message["statusCode"] = "XX";

        var result = CarrierTrackingMap.Map(message.ToJsonString(), "CAR-0001");

        result.IsMapped.Should().BeFalse();
        result.UnknownStatusCode.Should().Be("XX");
    }

    [Fact]
    public void Map_AMessageWithNoReadableStatusCode_IsNotMapped()
    {
        var result = CarrierTrackingMap.Map("not json", "CAR-0001");

        result.IsMapped.Should().BeFalse();
        result.UnknownStatusCode.Should().BeNull();
    }

    [Fact]
    public void ToTrackingEvent_AnUnknownStatusCode_StillThrows_SoTheWorkflowsActionFails()
    {
        var act = () => CarrierTrackingMap.ToTrackingEvent("""{"statusCode":"XX"}""", "CAR-0001");

        act.Should().Throw<UnknownStatusCodeException>().Which.StatusCode.Should().Be("XX");
    }
}
