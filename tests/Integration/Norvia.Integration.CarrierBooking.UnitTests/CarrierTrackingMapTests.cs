using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class CarrierTrackingMapTests
{
    private const string CarrierId = "CAR-0001";

    private static JsonObject Map(string json) => CarrierTrackingMap.ToTrackingEvent(json, CarrierId);

    private static string Message(string statusCode = "PU", string eventTime = "2030-10-02T08:45:00", string country = "NLD", string extra = "") =>
        $$"""
        { "bookingRef": "R-1", "eventId": "E-1", "statusCode": "{{statusCode}}", "eventTime": "{{eventTime}}",
          "place": "Rotterdam", "postcode": "3011 AA", "country": "{{country}}" {{extra}} }
        """;

    [Theory]
    [InlineData("PU", "Collected")]
    [InlineData("DEP", "Departed")]
    [InlineData("ARR", "Arrived")]
    [InlineData("OFD", "OutForDelivery")]
    [InlineData("POD", "Delivered")]
    [InlineData("NPU", "CollectionFailed")]
    [InlineData("NDL", "DeliveryFailed")]
    [InlineData("DMG", "Damaged")]
    public void ToTrackingEvent_EveryCodeOfTheSharedList_MapsToItsTrackingEventType(string code, string type)
    {
        Map(Message(code))["type"]!.GetValue<string>().Should().Be(type);
    }

    [Theory]
    [InlineData("DLV")]
    [InlineData("pu")]
    public void ToTrackingEvent_ACodeNotInTheList_Fails(string code)
    {
        var act = () => Map(Message(code));

        act.Should().Throw<UnknownStatusCodeException>().Which.StatusCode.Should().Be(code);
    }

    [Fact]
    public void ToTrackingEvent_NoStatusCode_Fails()
    {
        var act = () => Map("""{ "bookingRef": "R-1", "eventId": "E-1" }""");

        act.Should().Throw<UnknownStatusCodeException>();
    }

    [Theory]
    [InlineData("2030-01-15T10:00:00", "NLD", "2030-01-15T10:00:00+01:00")]
    [InlineData("2030-07-15T10:00:00", "NLD", "2030-07-15T10:00:00+02:00")]
    [InlineData("2030-07-15T10:00:00", "GBR", "2030-07-15T10:00:00+01:00")]
    [InlineData("2030-01-15T10:00:00", "GBR", "2030-01-15T10:00:00+00:00")]
    public void ToTrackingEvent_ALocalEventTime_IsReadInTheLocationsTimeZone(string local, string country, string expected)
    {
        Map(Message(eventTime: local, country: country))["eventTime"]!.GetValue<string>().Should().Be(expected);
    }

    [Fact]
    public void ToTrackingEvent_TheEta_IsReadInTheLocationsTimeZone()
    {
        // The message holds no delivery address, so the location's time zone stands in for it.
        var tracking = Map(Message(country: "GBR", extra: """, "eta": "2030-07-15T16:00:00" """));

        tracking["eta"]!.GetValue<string>().Should().Be("2030-07-15T16:00:00+01:00");
    }

    [Theory]
    [InlineData("DEU", "DE")]
    [InlineData("NLD", "NL")]
    [InlineData("GBR", "GB")]
    [InlineData("CHE", "CH")]
    public void ToTrackingEvent_AnAlpha3Country_BecomesAlpha2(string alpha3, string alpha2)
    {
        Map(Message(country: alpha3))["location"]!["countryCode"]!.GetValue<string>().Should().Be(alpha2);
    }

    [Fact]
    public void ToTrackingEvent_TheCarrierIsTheOneOfTheToken_NeverOneThePayloadNames()
    {
        var tracking = Map(Message(extra: """, "carrierId": "CAR-0002" """));

        tracking["source"]!["carrierId"]!.GetValue<string>().Should().Be(CarrierId);
        tracking.ToJsonString().Should().NotContain("CAR-0002");
    }

    [Fact]
    public void ToTrackingEvent_ValuesTheMapCantReadAreLeftOutForTheTmsToRefuse()
    {
        var tracking = Map("""{ "statusCode": "PU", "eventId": "E-1", "eventTime": "yesterday", "place": "Rotterdam", "postcode": "3011 AA", "country": "XXX", "eta": "soon" }""");

        tracking.ContainsKey("eventTime").Should().BeFalse();
        tracking.ContainsKey("eta").Should().BeFalse();
        tracking.ContainsKey("carrierBookingReference").Should().BeFalse();
        tracking["location"]!.AsObject().ContainsKey("countryCode").Should().BeFalse();
    }

    [Fact]
    public void ToTrackingEvent_TextThatIsNotJson_Fails()
    {
        var act = () => Map("not json");

        act.Should().Throw<UnknownStatusCodeException>();
    }

    [Fact]
    public void ToTrackingEvent_ABlankCarrierId_Throws()
    {
        var act = () => CarrierTrackingMap.ToTrackingEvent(Message(), " ");

        act.Should().Throw<ArgumentException>();
    }
}
