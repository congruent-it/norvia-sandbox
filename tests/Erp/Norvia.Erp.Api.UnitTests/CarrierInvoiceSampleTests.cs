using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norvia.Erp.Api.CarrierInvoices;

namespace Norvia.Erp.Api.UnitTests;

/// <summary>
/// The Carrier invoice Logic App sends the Canonical Model's Carrier invoice to the billing API, so the sample messages in
/// contracts/canonical-model/samples/carrier-invoice are what the API is called with. The options are those of Program.cs.
/// </summary>
public sealed class CarrierInvoiceSampleTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static TheoryData<string> Samples => new(
        typeof(CarrierInvoiceSampleTests).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("carrier-invoice-samples/", StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleMessage_IsAcceptedAsItIs(string sample)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(sample)!;

        var request = JsonSerializer.Deserialize<CarrierInvoiceRequest>(stream, Options);
        var (invoice, errors) = CarrierInvoiceValidator.Validate(request);

        errors.Should().BeEmpty();
        invoice.Should().NotBeNull();
    }

    [Fact]
    public void Samples_AreThere() => Samples.Should().HaveCount(2);

    [Fact]
    public void Deserialize_AMemberOutsideTheCanonicalModel_IsRefused()
    {
        var parse = () => JsonSerializer.Deserialize<CarrierInvoiceRequest>(
            """{"carrierId":"CAR-0001","invoiceNumber":"A","invoiceDate":"2026-10-19","correlationId":"c","lines":[],"extra":1}""", Options);

        parse.Should().Throw<JsonException>();
    }
}
