using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class CarrierTrackingFormatTests
{
    private static CarrierTrackingEvent Event(TrackingEventType type = TrackingEventType.Delivered, string country = "NLD", DateTimeOffset? at = null) => new(
        "evt-1", "VDT-77812", "CB-0000042", type, at ?? new DateTimeOffset(2030, 7, 2, 10, 15, 30, TimeSpan.Zero), "Utrecht", "3511 AW", country);

    [Fact]
    public void ToJson_IsTheCarrierTrackingFormat_WithoutNamingTheCarrier()
    {
        var json = CarrierTrackingFormat.ToJson(Event());

        JsonNode.DeepEquals(json, JsonNode.Parse("""
            {
              "bookingRef": "VDT-77812", "carrierBookingNumber": "CB-0000042", "eventId": "evt-1", "statusCode": "POD",
              "eventTime": "2030-07-02T12:15:30", "place": "Utrecht", "postcode": "3511 AW", "country": "NLD"
            }
            """)).Should().BeTrue(json.ToJsonString());
        Contracts.CarrierTrackingSchemaErrors(json).Should().BeEmpty();
        json.ToJsonString().Should().NotContain("CAR-");
    }

    [Theory]
    [InlineData(TrackingEventType.Collected, "PU")]
    [InlineData(TrackingEventType.Departed, "DEP")]
    [InlineData(TrackingEventType.Arrived, "ARR")]
    [InlineData(TrackingEventType.OutForDelivery, "OFD")]
    [InlineData(TrackingEventType.Delivered, "POD")]
    [InlineData(TrackingEventType.CollectionFailed, "NPU")]
    [InlineData(TrackingEventType.DeliveryFailed, "NDL")]
    [InlineData(TrackingEventType.Damaged, "DMG")]
    public void ToJson_UsesTheSharedStatusCodes(TrackingEventType type, string code)
    {
        var json = CarrierTrackingFormat.ToJson(Event(type));

        json["statusCode"]!.GetValue<string>().Should().Be(code);
        Contracts.CarrierTrackingSchemaErrors(json).Should().BeEmpty();
    }

    [Theory]
    [InlineData("NLD", "2030-07-02T12:15:30")]
    [InlineData("DEU", "2030-07-02T12:15:30")]
    [InlineData("GBR", "2030-07-02T11:15:30")]
    [InlineData("FIN", "2030-07-02T13:15:30")]
    public void ToJson_TheEventTimeIsLocalToTheCountry(string country, string local) =>
        CarrierTrackingFormat.ToJson(Event(country: country))["eventTime"]!.GetValue<string>().Should().Be(local);
}
