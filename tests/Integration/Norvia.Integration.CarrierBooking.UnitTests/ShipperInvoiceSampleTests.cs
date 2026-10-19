using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>
/// The sample messages in contracts/canonical-model/samples/shipper-invoice-issued: the event, and the invoice the map makes of it. CI runs
/// them without Azure.
/// </summary>
public sealed class ShipperInvoiceSampleTests
{
    private const string Folder = "CanonicalModel/samples/shipper-invoice-issued";

    public static TheoryData<string> Samples => new(Contracts.FileNames(Folder).Select(name => name[..^".json".Length]));

    private static string Message(string sample) => Contracts.Read($"{Folder}/{sample}.json");

    [Fact]
    public void Samples_CoverAnInvoiceWithAMinimumChargeAdjustmentAndOneWithASingleFreightLine() =>
        Contracts.FileNames(Folder).Should().BeEquivalentTo("freight-and-minimum-charge.json", "single-freight-line.json");

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleEvent_KeepsToTheCanonicalSchema(string sample)
    {
        var message = JsonNode.Parse(Message(sample))!.AsObject();
        message["eventType"]!.GetValue<string>().Should().Be("ShipperInvoiceIssued");
        message["eventVersion"]!.GetValue<int>().Should().Be(1);
        message["correlationId"]!.GetValue<string>().Should().Be(message["invoice"]!["correlationId"]!.GetValue<string>());

        var results = Schemas.InvoiceDocument.Validate(message["invoice"]);

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Fact]
    public void ToShipperInvoice_TheFirstSample_GivesTheSampleInvoiceOfThePartnerApi()
    {
        var expected = JsonNode.Parse(Contracts.Read("PartnerApi/samples/shipper-invoices/invoice.shipper-invoice.json"));

        var invoice = ShipperInvoiceMap.ToShipperInvoice(Message("freight-and-minimum-charge"));

        JsonNode.DeepEquals(invoice, expected).Should().BeTrue($"the map gave {invoice?.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToShipperInvoice_SampleEvent_ValidatesAgainstTheShipperInvoiceSchema(string sample)
    {
        var invoice = ShipperInvoiceMap.ToShipperInvoice(Message(sample));

        var results = Schemas.ShipperInvoice.Validate(invoice);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToShipperInvoice_SampleEvent_CarriesNoCarrierOrShipmentNumber(string sample)
    {
        var text = ShipperInvoiceMap.ToShipperInvoice(Message(sample))!.ToJsonString();

        text.Should().NotContain("SH-0").And.NotContain("shipmentNumber").And.NotContain("CAR-").And.NotContain("arrier");
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToShipperInvoice_SampleEvent_KeepsTheAmountsTheErpGave(string sample)
    {
        var issued = JsonNode.Parse(Message(sample))!["invoice"]!;

        var invoice = ShipperInvoiceMap.ToShipperInvoice(Message(sample))!;

        invoice["totalWithoutVat"]!.GetValue<decimal>().Should().Be(issued["totalWithoutVat"]!.GetValue<decimal>());
        invoice["vat"]!.GetValue<decimal>().Should().Be(issued["vat"]!.GetValue<decimal>());
        invoice["totalWithVat"]!.GetValue<decimal>().Should().Be(issued["totalWithVat"]!.GetValue<decimal>());
        invoice["lines"]!.AsArray().Select(line => line!["lineTotal"]!.GetValue<decimal>())
            .Should().Equal(issued["lines"]!.AsArray().Select(line => line!["total"]!.GetValue<decimal>()));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToShipperInvoice_TheMessageAsTheTriggerGivesIt_IsTheSameInvoice(string sample)
    {
        // The Logic Apps trigger gives a message's content base64-encoded (ADR 0019).
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Message(sample)));

        var fromEncoded = ShipperInvoiceMap.ToShipperInvoice(MessageContent.Text(encoded));

        JsonNode.DeepEquals(fromEncoded, ShipperInvoiceMap.ToShipperInvoice(Message(sample))).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Tracking_SampleMessage_GivesTheKeysTheWorkflowTracks(string sample)
    {
        var message = JsonNode.Parse(Message(sample))!;
        var inputs = ShipperInvoiceLogicApp.ShipperInvoiceWorkflow.Action(ShipperInvoiceLogicApp.ShipperInvoiceWorkflow.Tracking)["inputs"]!.AsObject();

        foreach (var (key, expression) in inputs)
        {
            Resolve(message, expression!.GetValue<string>()).Should().NotBeNullOrWhiteSpace($"the workflow tracks {key}");
        }
    }

    /// <summary>What a tracking expression of the workflow gives for a message: the first of its <c>json(...)</c> paths that the message has.</summary>
    internal static string? Resolve(JsonNode message, string expression) =>
        expression.Split("json(base64ToString(triggerBody()?['contentData']))", StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => System.Text.RegularExpressions.Regex.Matches(segment, @"\?\['(\w+)'\]").Select(match => match.Groups[1].Value).ToList())
            .Where(path => path.Count > 0)
            .Select(path => path.Aggregate((JsonNode?)message, (current, field) => current?[field]))
            .Select(node => node?.GetValue<string>())
            .FirstOrDefault(value => value is not null);
}
