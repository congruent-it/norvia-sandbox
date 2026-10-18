using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norvia.Erp.Api.BillableLines;

namespace Norvia.Erp.Api.UnitTests;

/// <summary>
/// The Billing Logic App sends the message body to the billing API as it is, so the sample messages in
/// contracts/canonical-model/samples/transport-order-billable are what the API is called with. The options are those of Program.cs.
/// </summary>
public sealed class TransportOrderBillableSampleTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public static TheoryData<string> Samples => new(
        typeof(TransportOrderBillableSampleTests).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("samples/", StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleMessage_IsAcceptedAsItIs(string sample)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(sample)!;

        var request = JsonSerializer.Deserialize<TransportOrderBillableRequest>(stream, Options);
        var (line, errors) = TransportOrderBillableValidator.Validate(request);

        errors.Should().BeEmpty();
        line.Should().NotBeNull();
    }

    [Fact]
    public void Samples_AreThere() => Samples.Should().HaveCount(3);
}
