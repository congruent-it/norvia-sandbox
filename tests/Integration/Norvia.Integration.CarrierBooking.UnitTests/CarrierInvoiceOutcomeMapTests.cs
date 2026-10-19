using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class CarrierInvoiceOutcomeMapTests
{
    private static JsonObject Approved() =>
        JsonNode.Parse(Contracts.Read("CanonicalModel/samples/carrier-invoice-approved/adjusted-and-rejected.json"))!.AsObject();

    private static JsonObject? Map(JsonObject approved) => CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(approved.ToJsonString());

    [Fact]
    public void ToCarrierInvoiceOutcome_GivesTheCarriersInvoiceNumberAndTheApprovedAmount()
    {
        var outcome = Map(Approved())!;

        outcome["invoiceNo"]!.GetValue<string>().Should().Be("VDT-2030-0413");
        outcome["approvedAmount"]!.GetValue<decimal>().Should().Be(275.50m);
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_AMatchedLine_IsMatchedAtItsAcceptedAmount_WithoutARemark()
    {
        var line = Map(Approved())!["lines"]![0]!.AsObject();

        line["bookingRef"]!.GetValue<string>().Should().Be("TRX-998");
        line["result"]!.GetValue<string>().Should().Be("MATCHED");
        line["acceptedAmount"]!.GetValue<decimal>().Should().Be(70.50m);
        line.ContainsKey("remark").Should().BeFalse();
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_AnAcceptedLineAtAnotherAmount_IsAcceptedAdjustedWithFinancesRemark()
    {
        var line = Map(Approved())!["lines"]![1]!.AsObject();

        line["result"]!.GetValue<string>().Should().Be("ACCEPTED_ADJUSTED");
        line["acceptedAmount"]!.GetValue<decimal>().Should().Be(110.00m);
        line["remark"]!.GetValue<string>().Should().Be("Waiting time is not part of the agreed price.");
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_AnAcceptedLineAtTheInvoicedAmount_IsAccepted()
    {
        var line = Map(Approved())!["lines"]![2]!.AsObject();

        line["result"]!.GetValue<string>().Should().Be("ACCEPTED");
        line["acceptedAmount"]!.GetValue<decimal>().Should().Be(95.00m);
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_ARejectedLine_IsRejectedAtZero_WithFinancesReason()
    {
        var line = Map(Approved())!["lines"]![3]!.AsObject();

        line["result"]!.GetValue<string>().Should().Be("REJECTED");
        line["acceptedAmount"]!.GetValue<decimal>().Should().Be(0m);
        line["remark"]!.GetValue<string>().Should().Be("Invoiced on VDT-2030-0301 already.");
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_LeavesOutWhatIsNorviasOwn()
    {
        var text = Map(Approved())!.ToJsonString();

        text.Should().NotContain("CAR-0001").And.NotContain("5d2a8e37").And.NotContain("invoicedAmount").And.NotContain("Accepted\"");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void ToCarrierInvoiceOutcome_TextThatIsNotAnApprovedInvoice_IsUnreadable(string text) =>
        CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(text).Should().BeNull();

    [Fact]
    public void ToCarrierInvoiceOutcome_AnotherEventType_IsUnreadable()
    {
        var approved = Approved();
        approved["eventType"] = "CarrierInvoiceOutcomeNotDelivered";

        Map(approved).Should().BeNull();
    }

    [Theory]
    [InlineData("invoiceNumber")]
    [InlineData("approvedAmount")]
    [InlineData("lines")]
    public void ToCarrierInvoiceOutcome_AnInvoiceWithoutAField_IsUnreadable(string field)
    {
        var approved = Approved();
        approved.Remove(field);

        Map(approved).Should().BeNull();
    }

    [Theory]
    [InlineData("carrierBookingReference")]
    [InlineData("result")]
    [InlineData("invoicedAmount")]
    public void ToCarrierInvoiceOutcome_ALineWithoutAField_IsUnreadable(string field)
    {
        var approved = Approved();
        approved["lines"]![0]!.AsObject().Remove(field);

        Map(approved).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ToCarrierInvoiceOutcome_AnAcceptedOrMatchedLineWithoutAnAcceptedAmount_IsUnreadable(int line)
    {
        var approved = Approved();
        approved["lines"]![line]!["acceptedAmount"] = null;

        Map(approved).Should().BeNull();
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_ALineOfAnUnknownResult_IsUnreadable()
    {
        var approved = Approved();
        approved["lines"]![0]!["result"] = "Waived";

        Map(approved).Should().BeNull();
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_AnInvoiceWithoutLines_IsUnreadable()
    {
        var approved = Approved();
        approved["lines"] = new JsonArray();

        Map(approved).Should().BeNull();
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_ALineThatIsNotAnObject_IsUnreadable()
    {
        var approved = Approved();
        approved["lines"] = new JsonArray("a line");

        Map(approved).Should().BeNull();
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_AnInvoiceNumberTheCarrierCouldNotHaveSent_IsUnreadable()
    {
        var approved = Approved();
        approved["invoiceNumber"] = new string('1', 51);

        Map(approved).Should().BeNull();
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_ARemarkTooLongForTheFormat_IsUnreadable()
    {
        var approved = Approved();
        approved["lines"]![1]!["remark"] = new string('x', 501);

        Map(approved).Should().BeNull();
    }
}
