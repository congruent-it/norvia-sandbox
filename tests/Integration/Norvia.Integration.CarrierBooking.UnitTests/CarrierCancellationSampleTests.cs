using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The sample pairs in contracts/partner-api/samples/carrier-cancellations: the event, and the cancellation the map makes of it.</summary>
public sealed class CarrierCancellationSampleTests
{
    private const string Folder = "PartnerApi/samples/carrier-cancellations";
    private const string Suffix = ".carrier-cancellation.json";

    public static TheoryData<string> Samples => new(SampleNames);

    private static IEnumerable<string> SampleNames =>
        Contracts.FileNames(Folder).Where(name => name.EndsWith(Suffix, StringComparison.Ordinal)).Select(name => name[..^Suffix.Length]);

    private static string EventOf(string sample) =>
        Contracts.Read($"{Folder}/{Contracts.FileNames(Folder).Single(name => name.StartsWith(sample + ".carrier-booking-", StringComparison.Ordinal))}");

    private static Schemas EventSchema(string sample) =>
        sample == "cancelled" ? Schemas.CarrierBookingCancelled : Schemas.CarrierBookingWithdrawn;

    [Fact]
    public void Samples_CoverBothKinds() =>
        SampleNames.Should().BeEquivalentTo(["cancelled", "withdrawn"]);

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierCancellation_SampleEvent_GivesTheSampleCancellation(string sample)
    {
        var expected = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}{Suffix}"));

        var cancellation = CarrierCancellationMap.ToCarrierCancellation(EventOf(sample));

        JsonNode.DeepEquals(cancellation, expected).Should().BeTrue($"the map gave {cancellation?.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleEvent_KeepsToTheCanonicalSchema(string sample)
    {
        var results = EventSchema(sample).Validate(JsonNode.Parse(EventOf(sample)));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleCancellation_KeepsToTheCarrierCancellationSchema(string sample)
    {
        var results = Schemas.CarrierCancellation.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}{Suffix}")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierCancellation_SampleEvent_KeepsToTheSchema(string sample)
    {
        var results = Schemas.CarrierCancellation.Validate(CarrierCancellationMap.ToCarrierCancellation(EventOf(sample)));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Fact]
    public void Validate_ACancelledWithoutAReferenceOrReason_BreaksTheCarrierCancellationSchema()
    {
        foreach (var name in new[] { "carrierBookingReference", "reasonText" })
        {
            var cancellation = JsonNode.Parse(Contracts.Read($"{Folder}/cancelled{Suffix}"))!.AsObject();
            cancellation.Remove(name);

            Schemas.CarrierCancellation.Validate(cancellation).IsValid.Should().BeFalse(name);
        }
    }

    [Fact]
    public void Validate_AWithdrawnWithAReference_BreaksTheCarrierCancellationSchema()
    {
        var cancellation = JsonNode.Parse(Contracts.Read($"{Folder}/withdrawn{Suffix}"))!.AsObject();
        cancellation["carrierBookingReference"] = "KESSLER-88213";

        Schemas.CarrierCancellation.Validate(cancellation).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ACancellationNamingTheCarrierOrException_BreaksTheCarrierCancellationSchema()
    {
        var cancellation = JsonNode.Parse(Contracts.Read($"{Folder}/cancelled{Suffix}"))!.AsObject();
        cancellation["exceptionNumber"] = "EX-0000001";

        Schemas.CarrierCancellation.Validate(cancellation).IsValid.Should().BeFalse();
    }
}
