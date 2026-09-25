using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake.UnitTests;

/// <summary>Runs the intake map on each sample partner booking in contracts/partner-api/samples/bookings.</summary>
public sealed class IntakeMapTests
{
    private const string ShipperId = "SHP-0001";

    public static TheoryData<string> Samples => new(PartnerApiSamples.BookingNames());

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToTransportOrder_Sample_GivesTheExpectedTransportOrder(string sample)
    {
        var transportOrder = IntakeMap.ToTransportOrder(PartnerApiSamples.Booking(sample), ShipperId);

        var expected = PartnerApiSamples.ExpectedTransportOrder(sample);
        JsonNode.DeepEquals(transportOrder, expected).Should().BeTrue(
            $"the map should give{Environment.NewLine}{expected}{Environment.NewLine}but gave{Environment.NewLine}{transportOrder}");
    }

    [Fact]
    public void ToTransportOrder_BookingAsText_GivesTheExpectedTransportOrder()
    {
        // The Logic App passes the booking to the map as the JSON text the Shipper sent.
        var transportOrder = IntakeMap.ToTransportOrder(PartnerApiSamples.Booking("full").ToJsonString(), ShipperId);

        JsonNode.DeepEquals(transportOrder, PartnerApiSamples.ExpectedTransportOrder("full")).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not JSON")]
    [InlineData("""["a list, not a booking"]""")]
    public void ToTransportOrder_TextThatIsNoBooking_GivesOnlyTheShipper(string partnerBooking)
    {
        // The map never rejects a booking: the TMS reports everything that is then missing, with its own codes.
        var transportOrder = IntakeMap.ToTransportOrder(partnerBooking, ShipperId);

        transportOrder.ToJsonString().Should().Be("""{"shipperId":"SHP-0001"}""");
    }

    [Fact]
    public void ToTransportOrder_ConvertedUnits_WritesNumbersWithoutTrailingZeros()
    {
        // The Logic App's run history shows the mapped message, so 2.5 t must read as 2500 kg, not 2500.0.
        var transportOrder = IntakeMap.ToTransportOrder(PartnerApiSamples.Booking("units"), ShipperId);

        var pallets = transportOrder["goodsLines"]![0]!;
        pallets["weightKg"]!.ToJsonString().Should().Be("2500");
        pallets["dimensionsCm"]!["length"]!.ToJsonString().Should().Be("120");
    }

    public static TheoryData<string> CountrySpellings => new(PartnerApiSamples.CountrySpellings());

    [Theory]
    [MemberData(nameof(CountrySpellings))]
    public void ToTransportOrder_CountrySpellingTheBookingSchemaAllows_GivesItsCodeAndLocalTime(string spelling)
    {
        var booking = new JsonObject { ["pickupCountry"] = spelling, ["pickupFrom"] = "01-10-2030 08:00" };

        var collection = IntakeMap.ToTransportOrder(booking, ShipperId)["collection"];

        var country = collection?["address"]?["country"]?.GetValue<string>();
        country.Should().MatchRegex("^[A-Z]{2}$", $"the map should know the country {spelling}");
        var earliest = collection?["window"]?["earliest"];
        earliest.Should().NotBeNull($"the map should know the time zone of {spelling}");
    }

    [Fact]
    public void Spellings_TheMapKnows_AreExactlyTheOnesTheBookingSchemaAllows()
    {
        // The two lists live apart until a second partner format needs countries too, so neither may drift from the other.
        var schemaAllows = PartnerApiSamples.CountrySpellings().ToHashSet(StringComparer.Ordinal);
        var mapKnows = Countries.Spellings.ToHashSet(StringComparer.Ordinal);

        mapKnows.Except(schemaAllows).Should().BeEmpty("the booking schema should allow every spelling the map knows");
        schemaAllows.Except(mapKnows).Should().BeEmpty("the map should know every spelling the booking schema allows");
    }

    [Fact]
    public void ToTransportOrder_IsoCode_GivesTheSameCode()
    {
        var booking = new JsonObject { ["pickupCountry"] = "PT" };

        IntakeMap.ToTransportOrder(booking, ShipperId)["collection"]!["address"]!["country"]!.GetValue<string>().Should().Be("PT");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToTransportOrder_Sample_GivesAValidCanonicalTransportOrder(string sample)
    {
        var transportOrder = IntakeMap.ToTransportOrder(PartnerApiSamples.Booking(sample), ShipperId);

        var results = Schemas.CanonicalTransportOrder.Validate(transportOrder);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }
}
