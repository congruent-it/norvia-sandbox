using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norvia.Erp.Api.CarrierCosts;

namespace Norvia.Erp.Api.UnitTests;

/// <summary>
/// The Carrier cost Logic App sends the message body to the billing API as it is, so the sample messages in
/// contracts/canonical-model/samples/carrier-cost-incurred are what the API is called with. The options are those of Program.cs.
/// </summary>
public sealed class CarrierCostIncurredSampleTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public static TheoryData<string> Samples => new(
        typeof(CarrierCostIncurredSampleTests).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("carrier-cost-samples/", StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleMessage_IsAcceptedAsItIs(string sample)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(sample)!;

        var request = JsonSerializer.Deserialize<CarrierCostIncurredRequest>(stream, Options);
        var (cost, errors) = CarrierCostIncurredValidator.Validate(request);

        errors.Should().BeEmpty();
        cost.Should().NotBeNull();
    }

    [Fact]
    public void Samples_AreThere() => Samples.Should().HaveCount(2);
}
