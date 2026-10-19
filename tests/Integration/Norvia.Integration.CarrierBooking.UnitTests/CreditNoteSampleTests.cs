using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The sample messages in contracts/canonical-model/samples/credit-note-issued keep to the Canonical Model. CI runs them without Azure.</summary>
public sealed class CreditNoteSampleTests
{
    private const string Folder = "CanonicalModel/samples/credit-note-issued";

    public static TheoryData<string> Samples => new(Contracts.FileNames(Folder).Select(name => name[..^".json".Length]));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleEvent_KeepsToTheCanonicalSchema(string sample)
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}.json"))!;

        message["eventType"]!.GetValue<string>().Should().Be("CreditNoteIssued");
        message["eventVersion"]!.GetValue<int>().Should().Be(1);

        var results = Schemas.CreditNoteDocument.Validate(message["creditNote"]);

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
        message["correlationId"]!.GetValue<string>().Should().Be(message["creditNote"]!["correlationId"]!.GetValue<string>());
    }

    [Fact]
    public void Validate_ACreditNoteWithoutAReason_BreaksTheCanonicalSchema()
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/partial-credit.json"))!;
        message["creditNote"]!.AsObject().Remove("reason");

        Schemas.CreditNoteDocument.Validate(message["creditNote"]).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ASampleCreditNote_AgreesWithThePartnerApiSampleOnItsAmounts()
    {
        var message = JsonNode.Parse(Contracts.Read($"{Folder}/partial-credit.json"))!["creditNote"]!;
        var partner = JsonNode.Parse(Contracts.Read("PartnerApi/samples/shipper-invoices/credit-note.shipper-invoice.json"))!;

        message["creditNoteNumber"]!.GetValue<string>().Should().Be(partner["documentNumber"]!.GetValue<string>());
        message["invoiceNumber"]!.GetValue<string>().Should().Be(partner["creditedInvoiceNumber"]!.GetValue<string>());
        message["transportOrderNumber"]!.GetValue<string>().Should().Be(partner["creditedTransportOrderNumber"]!.GetValue<string>());
        message["totalWithoutVat"]!.GetValue<decimal>().Should().Be(partner["totalWithoutVat"]!.GetValue<decimal>());
        message["vat"]!.GetValue<decimal>().Should().Be(partner["vat"]!.GetValue<decimal>());
        message["reason"]!.GetValue<string>().Should().Be(partner["reason"]!.GetValue<string>());
    }
}
