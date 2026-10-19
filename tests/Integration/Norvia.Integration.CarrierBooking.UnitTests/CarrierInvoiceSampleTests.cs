using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The sample pairs in contracts/partner-api/samples/carrier-invoices: a Carrier's invoice, and the Canonical Model submission the map makes of it for Carrier CAR-0001, and the sample answers.</summary>
public sealed class CarrierInvoiceSampleTests
{
    private const string Folder = "PartnerApi/samples/carrier-invoices";
    private const string ResponseFolder = "PartnerApi/samples/carrier-invoice-responses";
    private const string CarrierId = "CAR-0001";
    private const string Correlation = "1c7b0d3e-5a54-4f6a-9d5f-6a2f3c0d9a11";

    public static TheoryData<string> Samples => new(
        Contracts.FileNames(Folder).Where(name => name.EndsWith(".carrier-invoice.json", StringComparison.Ordinal))
            .Select(name => name[..^".carrier-invoice.json".Length]));

    public static TheoryData<string> Responses => new(Contracts.FileNames(ResponseFolder).Select(name => name[..^".json".Length]));

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierInvoiceSubmission_SampleInvoice_GivesTheSampleCanonicalSubmission(string sample)
    {
        var expected = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-invoice-submission.json"));

        var mapped = CarrierInvoiceMap.ToCarrierInvoiceSubmission(Contracts.Read($"{Folder}/{sample}.carrier-invoice.json"), CarrierId, Correlation);

        JsonNode.DeepEquals(mapped.ErpRequest, expected).Should().BeTrue($"the map gave {mapped.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleInvoice_KeepsToTheCarrierInvoiceSchema(string sample)
    {
        var results = Schemas.CarrierInvoice.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-invoice.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierInvoiceSubmission_SampleInvoice_KeepsToTheCanonicalSchema(string sample)
    {
        var mapped = CarrierInvoiceMap.ToCarrierInvoiceSubmission(Contracts.Read($"{Folder}/{sample}.carrier-invoice.json"), CarrierId, Correlation);

        var results = Schemas.CarrierInvoiceSubmission.Validate(mapped.ErpRequest);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleCanonicalSubmission_KeepsToTheCanonicalSchema(string sample)
    {
        var results = Schemas.CarrierInvoiceSubmission.Validate(JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.carrier-invoice-submission.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Responses))]
    public void Validate_SampleResponse_KeepsToTheCarrierInvoiceResponseSchema(string sample)
    {
        var results = Schemas.CarrierInvoiceResponse.Validate(JsonNode.Parse(Contracts.Read($"{ResponseFolder}/{sample}.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [InlineData("carrierId", "CAR-0002")]
    [InlineData("correlationId", "mine")]
    public void Validate_AnInvoiceNamingTheCarrierOrACorrelationId_BreaksTheCarrierInvoiceSchema(string property, string value)
    {
        var invoice = JsonNode.Parse(Contracts.Read($"{Folder}/two-lines.carrier-invoice.json"))!.AsObject();
        invoice[property] = value;

        Schemas.CarrierInvoice.Validate(invoice).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_AnInvoiceWithoutLines_BreaksTheCarrierInvoiceSchema()
    {
        var invoice = JsonNode.Parse(Contracts.Read($"{Folder}/two-lines.carrier-invoice.json"))!.AsObject();
        invoice["lines"] = new JsonArray();

        Schemas.CarrierInvoice.Validate(invoice).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ALineWithAnAmountOfZero_BreaksTheCarrierInvoiceSchema()
    {
        var invoice = JsonNode.Parse(Contracts.Read($"{Folder}/two-lines.carrier-invoice.json"))!.AsObject();
        invoice["lines"]![0]!["amount"] = 0;

        Schemas.CarrierInvoice.Validate(invoice).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_AnInvoiceDateInTheCanonicalFormat_BreaksTheCarrierInvoiceSchema()
    {
        var invoice = JsonNode.Parse(Contracts.Read($"{Folder}/two-lines.carrier-invoice.json"))!.AsObject();
        invoice["invoiceDate"] = "2026-10-19";

        Schemas.CarrierInvoice.Validate(invoice).IsValid.Should().BeFalse();
    }
}
