using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The sample pairs in contracts/partner-api/samples/carrier-answers: a Carrier's answer, and the Canonical Model answer the map makes of it for Carrier CAR-0001.</summary>
public sealed class CarrierAnswerSampleTests
{
    private const string Folder = "PartnerApi/samples/carrier-answers";
    private const string CarrierId = "CAR-0001";

    public static TheoryData<string> Samples => new(
        Contracts.FileNames(Folder).Where(name => name.EndsWith(".carrier-answer.json", StringComparison.Ordinal))
            .Select(name => name[..^".carrier-answer.json".Length]));

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierBookingAnswer_SampleAnswer_GivesTheSampleCanonicalAnswer(string sample)
    {
        var expected = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-booking-answer.json"));

        var mapped = CarrierAnswerMap.ToCarrierBookingAnswer(Contracts.Read($"{Folder}/{sample}.carrier-answer.json"), CarrierId);

        JsonNode.DeepEquals(mapped.TmsRequest, expected).Should().BeTrue($"the map gave {mapped.ToJsonString()}");
        mapped.CarrierBookingNumber.Should().Be("CB-0000042");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleAnswer_KeepsToTheCarrierAnswerSchema(string sample)
    {
        var results = Schemas.CarrierAnswer.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-answer.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierBookingAnswer_SampleAnswer_KeepsToTheCanonicalSchema(string sample)
    {
        var mapped = CarrierAnswerMap.ToCarrierBookingAnswer(Contracts.Read($"{Folder}/{sample}.carrier-answer.json"), CarrierId);

        var results = Schemas.CarrierBookingAnswer.Validate(mapped.TmsRequest);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleCanonicalAnswer_KeepsToTheCanonicalSchema(string sample)
    {
        var results = Schemas.CarrierBookingAnswer.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-booking-answer.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Fact]
    public void Validate_AConfirmationWithoutAReference_BreaksTheCarrierAnswerSchema()
    {
        var answer = JsonNode.Parse(Contracts.Read($"{Folder}/confirmed.carrier-answer.json"))!.AsObject();
        answer.Remove("bookingRef");

        Schemas.CarrierAnswer.Validate(answer).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ARejectionForOtherWithoutText_BreaksTheCarrierAnswerSchema()
    {
        var answer = JsonNode.Parse(Contracts.Read($"{Folder}/declined-other.carrier-answer.json"))!.AsObject();
        answer.Remove("declineText");

        Schemas.CarrierAnswer.Validate(answer).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_AnAnswerNamingTheCarrier_BreaksTheCarrierAnswerSchema()
    {
        var answer = JsonNode.Parse(Contracts.Read($"{Folder}/confirmed.carrier-answer.json"))!.AsObject();
        answer["carrierId"] = "CAR-0002";

        Schemas.CarrierAnswer.Validate(answer).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ACanonicalRejectionForOtherWithoutText_BreaksTheCanonicalSchema()
    {
        var answer = JsonNode.Parse(Contracts.Read($"{Folder}/declined-other.carrier-booking-answer.json"))!.AsObject();
        answer.Remove("reasonText");

        Schemas.CarrierBookingAnswer.Validate(answer).IsValid.Should().BeFalse();
    }
}
