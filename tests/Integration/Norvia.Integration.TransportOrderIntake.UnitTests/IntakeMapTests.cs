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
    public void ToTransportOrder_ConvertedUnits_WritesNumbersWithoutTrailingZeros()
    {
        // The Logic App's run history shows the mapped message, so 2.5 t must read as 2500 kg, not 2500.0.
        var transportOrder = IntakeMap.ToTransportOrder(PartnerApiSamples.Booking("units"), ShipperId);

        var pallets = transportOrder["goodsLines"]![0]!;
        pallets["weightKg"]!.ToJsonString().Should().Be("2500");
        pallets["dimensionsCm"]!["length"]!.ToJsonString().Should().Be("120");
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
