using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The sample pairs in contracts/partner-api/samples/carrier-proofs-of-delivery: a Carrier's Proof of delivery, and the Canonical Model metadata the map makes of it for Carrier CAR-0001.</summary>
public sealed class CarrierProofOfDeliverySampleTests
{
    private const string Folder = "PartnerApi/samples/carrier-proofs-of-delivery";
    private const string CarrierId = "CAR-0001";

    public static TheoryData<string> Samples => new(
        Contracts.FileNames(Folder).Where(name => name.EndsWith(".carrier-proof-of-delivery.json", StringComparison.Ordinal))
            .Select(name => name[..^".carrier-proof-of-delivery.json".Length]));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Map_SampleMessage_GivesTheSampleMetadata(string sample)
    {
        var expected = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.proof-of-delivery.json"));

        var mapped = CarrierProofOfDeliveryMap.Map(Contracts.Read($"{Folder}/{sample}.carrier-proof-of-delivery.json"), CarrierId);

        JsonNode.DeepEquals(mapped.Metadata, expected).Should().BeTrue($"the map gave {mapped.Metadata.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Map_SampleMessage_DecodesEveryFile(string sample)
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-proof-of-delivery.json"))!;

        var mapped = CarrierProofOfDeliveryMap.Map(message.ToJsonString(), CarrierId);

        mapped.Files.Select(file => Convert.ToBase64String(file.Content))
            .Should().Equal(message["files"]!.AsArray().Select(file => file!["content"]!.GetValue<string>()));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleMessage_KeepsToTheCarrierProofOfDeliverySchema(string sample)
    {
        var results = Schemas.CarrierProofOfDelivery.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-proof-of-delivery.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Map_SampleMessage_KeepsToTheCanonicalSchema(string sample)
    {
        var mapped = CarrierProofOfDeliveryMap.Map(Contracts.Read($"{Folder}/{sample}.carrier-proof-of-delivery.json"), CarrierId);

        var results = Schemas.ProofOfDelivery.Validate(mapped.Metadata);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [InlineData("bookingRef")]
    [InlineData("documentId")]
    [InlineData("receivedBy")]
    [InlineData("deliveryTime")]
    [InlineData("place")]
    [InlineData("postcode")]
    [InlineData("country")]
    [InlineData("condition")]
    [InlineData("files")]
    public void Validate_AMessageWithoutARequiredField_BreaksTheCarrierProofOfDeliverySchema(string field)
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/clean-winter.carrier-proof-of-delivery.json"))!.AsObject();
        message.Remove(field);

        Schemas.CarrierProofOfDelivery.Validate(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ACanonicalProofOfDeliveryWithoutTheBookingReference_BreaksTheCanonicalSchema()
    {
        var metadata = JsonNode.Parse(Contracts.Read($"{Folder}/clean-winter.proof-of-delivery.json"))!.AsObject();
        metadata.Remove("carrierBookingReference");

        Schemas.ProofOfDelivery.Validate(metadata).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_AMessageNamingTheCarrier_BreaksTheCarrierProofOfDeliverySchema()
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/clean-winter.carrier-proof-of-delivery.json"))!.AsObject();
        message["carrierId"] = "CAR-0002";

        Schemas.CarrierProofOfDelivery.Validate(message).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ReservationsWithoutARemark_BreaksBothSchemas()
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/reservations-summer.carrier-proof-of-delivery.json"))!.AsObject();
        message.Remove("remark");
        var metadata = JsonNode.Parse(Contracts.Read($"{Folder}/reservations-summer.proof-of-delivery.json"))!.AsObject();
        metadata.Remove("remark");

        Schemas.CarrierProofOfDelivery.Validate(message).IsValid.Should().BeFalse();
        Schemas.ProofOfDelivery.Validate(metadata).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ACarrierMessageWithoutFiles_OrWithSix_BreaksTheCarrierProofOfDeliverySchema()
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/clean-winter.carrier-proof-of-delivery.json"))!.AsObject();
        var file = message["files"]![0]!.DeepClone();
        message["files"] = new JsonArray();
        Schemas.CarrierProofOfDelivery.Validate(message).IsValid.Should().BeFalse();

        message["files"] = new JsonArray(Enumerable.Range(0, 6).Select(_ => file.DeepClone()).ToArray());
        Schemas.CarrierProofOfDelivery.Validate(message).IsValid.Should().BeFalse();
    }
}
