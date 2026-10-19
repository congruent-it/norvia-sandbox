using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class CarrierInvoiceSchemaTests
{
    public static TheoryData<string> Outcomes() => new() { "approved-with-adjustment", "with-rejection" };

    private static JsonObject Outcome(string name) =>
        JsonNode.Parse(Contracts.Read($"PartnerApi/samples/carrier-invoice-outcomes/{name}.carrier-invoice-outcome.json"))!.AsObject();

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void AnOutcomeSample_FollowsTheSchema(string name) =>
        Contracts.CarrierInvoiceOutcomeSchemaErrors(Outcome(name)).Should().BeEmpty();

    [Fact]
    public void TheOutcomeSchema_ListsTheResultsThePortalTakes()
    {
        var schema = JsonNode.Parse(Contracts.Read("PartnerApi/carrier-invoice-outcome.v1.schema.json"))!;

        schema["properties"]!["lines"]!["items"]!["properties"]!["result"]!["enum"]!.AsArray()
            .Select(node => node!.GetValue<string>()).Should().BeEquivalentTo(CarrierInvoiceOutcomeResults.WireNames);
    }

    [Theory]
    [InlineData("invoiceNo", "")]
    [InlineData("approvedAmount", -1)]
    [InlineData("approvedAmount", 10.005)]
    [InlineData("carrierId", "CAR-0001")]
    public void AnOutcome_BreakingTheFormat_FailsTheSchema(string field, object value)
    {
        var outcome = Outcome("approved-with-adjustment");
        outcome[field] = JsonValue.Create(value);

        Contracts.CarrierInvoiceOutcomeSchemaErrors(outcome).Should().NotBeEmpty();
    }

    [Fact]
    public void AnOutcomeLine_WithAnUnknownResult_FailsTheSchema()
    {
        var outcome = Outcome("with-rejection");
        outcome["lines"]![0]!["result"] = "PRICE_MISMATCH";

        Contracts.CarrierInvoiceOutcomeSchemaErrors(outcome).Should().NotBeEmpty();
    }

    public static TheoryData<string> Responses() => new() { "received-approved", "received-repeated", "received-with-price-mismatch" };

    // What the portal reads of a received invoice is what the Logic App relays, so the samples of the response schema are the portal's test data.
    [Theory]
    [MemberData(nameof(Responses))]
    public void AReceivedResponseSample_IsReadAsReceived(string name)
    {
        var body = Contracts.Read($"PartnerApi/samples/carrier-invoice-responses/{name}.json");

        var reply = PartnerAnswers.ReadCarrierInvoiceReply(body.Contains("\"repeated\": true", StringComparison.Ordinal) ? 200 : 201, body, correlationId: null);

        reply.Result.Should().Be(CarrierInvoiceReplyResult.Received);
        reply.Receipt!.Lines.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("invoice-number-used", 409, CarrierInvoiceReplyResult.InvoiceNumberUsed)]
    [InlineData("carrier-not-found", 422, CarrierInvoiceReplyResult.CarrierNotFound)]
    [InlineData("invalid", 400, CarrierInvoiceReplyResult.Invalid)]
    [InlineData("not-received", 502, CarrierInvoiceReplyResult.NotReceived)]
    public void ARefusalSample_IsReadAsThatRefusal(string name, int status, CarrierInvoiceReplyResult expected)
    {
        var reply = PartnerAnswers.ReadCarrierInvoiceReply(status, Contracts.Read($"PartnerApi/samples/carrier-invoice-responses/{name}.json"), correlationId: null);

        reply.Result.Should().Be(expected);
    }
}
