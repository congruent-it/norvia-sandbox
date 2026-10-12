using System.Text.Json.Nodes;
using Norvia.Tms.Application.UnitTests.Trips;

namespace Norvia.Tms.Application.UnitTests.ProofsOfDelivery;

/// <summary>The Canonical Model's Proof of delivery schema against sample messages, from a Carrier and from Own fleet (P12).</summary>
public sealed class ProofOfDeliverySchemaTests
{
    public static TheoryData<string> SampleMessages => new(CanonicalSchemas.ProofOfDeliverySamples());

    [Theory]
    [MemberData(nameof(SampleMessages))]
    public void Schema_SampleMessage_Validates(string sample)
    {
        var results = CanonicalSchemas.ValidateProofOfDelivery(CanonicalSchemas.ReadSample(sample));

        results.IsValid.Should().BeTrue(CanonicalSchemas.Errors(results));
    }

    [Theory]
    [InlineData("tripNumber")]
    [InlineData("shipmentNumber")]
    [InlineData("tripVersion")]
    public void Schema_OwnFleetMessageWithoutATripPart_DoesNotValidate(string property)
    {
        var message = Sample("own-fleet-clean").AsObject();
        message.Remove(property);

        CanonicalSchemas.ValidateProofOfDelivery(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Schema_OwnFleetMessageWithoutADriverId_DoesNotValidate()
    {
        var message = Sample("own-fleet-clean").AsObject();
        message["source"]!.AsObject().Remove("driverId");

        CanonicalSchemas.ValidateProofOfDelivery(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Schema_OwnFleetMessageNamingACarrierBooking_DoesNotValidate()
    {
        var message = Sample("own-fleet-clean").AsObject();
        message["carrierBookingReference"] = "VDT-1";

        CanonicalSchemas.ValidateProofOfDelivery(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Schema_CarrierMessageWithoutABookingReference_OrNamingATrip_DoesNotValidate()
    {
        var without = Sample("carrier").AsObject();
        without.Remove("carrierBookingReference");
        var withTrip = Sample("carrier").AsObject();
        withTrip["tripNumber"] = "TR-0000012";

        CanonicalSchemas.ValidateProofOfDelivery(without).IsValid.Should().BeFalse();
        CanonicalSchemas.ValidateProofOfDelivery(withTrip).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Schema_ReservationsWithoutARemark_DoesNotValidate()
    {
        var message = Sample("own-fleet-reservations").AsObject();
        message.Remove("remark");

        CanonicalSchemas.ValidateProofOfDelivery(message).IsValid.Should().BeFalse();
    }

    private static JsonNode Sample(string name) =>
        CanonicalSchemas.ReadSample(CanonicalSchemas.ProofOfDeliverySamples().Single(sample => sample.EndsWith($"/{name}.json", StringComparison.Ordinal)));
}
