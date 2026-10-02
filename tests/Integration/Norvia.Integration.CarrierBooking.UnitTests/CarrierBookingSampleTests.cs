using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The sample pairs in contracts/partner-api/samples/carrier-bookings: a release, and the booking the map makes of it.</summary>
public sealed class CarrierBookingSampleTests
{
    private const string Folder = "PartnerApi/samples/carrier-bookings";

    public static TheoryData<string> Samples => new(
        Contracts.FileNames(Folder).Where(name => name.EndsWith(".shipment-released.json", StringComparison.Ordinal))
            .Select(name => name[..^".shipment-released.json".Length]));

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierBooking_SampleRelease_GivesTheSampleBooking(string sample)
    {
        var release = Contracts.Read($"{Folder}/{sample}.shipment-released.json");
        var expected = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-booking.json"));

        var booking = CarrierBookingMap.ToCarrierBooking(release);

        JsonNode.DeepEquals(booking, expected).Should().BeTrue($"the map gave {booking?.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleBooking_KeepsToTheCarrierBookingSchema(string sample)
    {
        var results = Schemas.CarrierBooking.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-booking.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierBooking_SampleRelease_KeepsToTheCarrierBookingSchema_AndNamesNoShipper(string sample)
    {
        var booking = CarrierBookingMap.ToCarrierBooking(Contracts.Read($"{Folder}/{sample}.shipment-released.json"));

        var results = Schemas.CarrierBooking.Validate(booking);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
        booking!.ToJsonString().Should().NotContain("SHP-").And.NotContain("BD-2030").And.NotContain("freightCharge");
    }

    [Fact]
    public void Validate_ABookingNamingTheShipper_BreaksTheCarrierBookingSchema()
    {
        var booking = JsonNode.Parse(Contracts.Read($"{Folder}/full.carrier-booking.json"))!.AsObject();
        booking["shipperId"] = "SHP-0001";

        Schemas.CarrierBooking.Validate(booking).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ABookingWithAnAlpha2CountryOrAnOffset_BreaksTheCarrierBookingSchema()
    {
        var booking = JsonNode.Parse(Contracts.Read($"{Folder}/full.carrier-booking.json"))!.AsObject();
        booking["collectionCountry"] = "NL";
        booking["collectFrom"] = "2030-10-02T07:30:00+02:00";

        Schemas.CarrierBooking.Validate(booking).IsValid.Should().BeFalse();
    }
}
