using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.UnitTests.CarrierInvoices;

public sealed class CarrierInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly CarrierId Carrier = new("CAR-0001");

    private static CarrierCost Cost(string number, string reference, decimal total, string carrierId = "CAR-0001") =>
        CarrierCost.Record(
            new CarrierCostData(
                number,
                reference,
                new CarrierId(carrierId),
                "SH-0000001",
                "TO-0000001",
                new CarrierCostCharge(total, [new ChargeLine(ChargeLineType.Freight, total)]),
                CarrierCostSource.FromRateCard("CRC-0001", 1),
                Now,
                "delivered"),
            Now).Cost!;

    private static CarrierInvoiceData Invoice(params CarrierInvoiceLineData[] lines) =>
        new(Carrier, "VDT-1", new DateOnly(2026, 10, 19), lines, "invoice-1");

    private static CarrierInvoiceLineData Line(string reference, decimal amount, string? description = null) => new(reference, amount, description);

    [Fact]
    public void Receive_WhereEveryLineMatches_IsApprovedAtOnceAndInvoicesTheCarrierCosts()
    {
        var costs = new[] { Cost("CB-0000001", "TRX-1", 70.50m), Cost("CB-0000002", "TRX-2", 20m) };

        var (invoice, refusal) = CarrierInvoice.Receive(Invoice(Line("TRX-1", 70.50m, "Utrecht"), Line("TRX-2", 20m)), costs, 0m, Now);

        refusal.Should().BeNull();
        invoice!.Status.Should().Be(CarrierInvoiceStatus.Approved);
        invoice.ApprovedAmount.Should().Be(90.50m);
        invoice.ApprovedAt.Should().Be(Now);
        invoice.Total.Should().Be(90.50m);
        invoice.CarrierId.Should().Be(Carrier);
        invoice.InvoiceNumber.Should().Be("VDT-1");
        invoice.CorrelationId.Should().Be("invoice-1");
        invoice.ReceivedAt.Should().Be(Now);
        invoice.Lines.Should().OnlyContain(line => line.Result == CarrierInvoiceLineResult.Matched && line.Status == CarrierInvoiceLineStatus.Matched);
        invoice.Lines.Select(line => line.AcceptedAmount).Should().Equal(70.50m, 20m);
        invoice.Lines.Select(line => line.CarrierBookingNumber).Should().Equal("CB-0000001", "CB-0000002");
        invoice.Lines[0].Description.Should().Be("Utrecht");
        costs.Should().OnlyContain(cost => cost.Status == CarrierCostStatus.Invoiced);
    }

    [Fact]
    public void Receive_AnAmountOutsideTheTolerance_IsAPriceMismatchThatWaitsForFinanceAndLeavesTheCostOpen()
    {
        var cost = Cost("CB-0000001", "TRX-1", 70m);

        var (invoice, _) = CarrierInvoice.Receive(Invoice(Line("TRX-1", 70.01m)), [cost], 0m, Now);

        invoice!.Status.Should().Be(CarrierInvoiceStatus.Received);
        invoice.ApprovedAmount.Should().BeNull();
        invoice.ApprovedAt.Should().BeNull();
        invoice.Lines.Single().Should().Match<CarrierInvoiceLine>(line =>
            line.Result == CarrierInvoiceLineResult.PriceMismatch && line.Status == CarrierInvoiceLineStatus.ToDecide
            && line.CarrierBookingNumber == "CB-0000001" && line.AcceptedAmount == null);
        cost.Status.Should().Be(CarrierCostStatus.Open);
    }

    [Theory]
    [InlineData(70.50, true)]
    [InlineData(71.50, true)]
    [InlineData(69.50, true)]
    [InlineData(71.51, false)]
    [InlineData(69.49, false)]
    public void Receive_WithATolerance_MatchesAnAmountUpToThatFarFromTheTotal(double amount, bool matches)
    {
        var (invoice, _) = CarrierInvoice.Receive(Invoice(Line("TRX-1", (decimal)amount)), [Cost("CB-0000001", "TRX-1", 70.50m)], 1m, Now);

        invoice!.Lines.Single().Result.Should().Be(matches ? CarrierInvoiceLineResult.Matched : CarrierInvoiceLineResult.PriceMismatch);
    }

    [Fact]
    public void Receive_AReferenceWithNoCarrierCost_IsAnUnknownBookingThatDoesNotFailTheInvoice()
    {
        var (invoice, refusal) = CarrierInvoice.Receive(Invoice(Line("TRX-9", 10m)), [], 0m, Now);

        refusal.Should().BeNull();
        invoice!.Status.Should().Be(CarrierInvoiceStatus.Received);
        invoice.Lines.Single().Should().Match<CarrierInvoiceLine>(line =>
            line.Result == CarrierInvoiceLineResult.UnknownBooking && line.Status == CarrierInvoiceLineStatus.ToDecide && line.CarrierBookingNumber == null);
    }

    [Fact]
    public void Receive_AnInvoicedCarrierCost_IsADuplicate()
    {
        var cost = Cost("CB-0000001", "TRX-1", 70m);
        cost.MarkInvoiced();

        var (invoice, _) = CarrierInvoice.Receive(Invoice(Line("TRX-1", 70m)), [cost], 0m, Now);

        invoice!.Lines.Single().Result.Should().Be(CarrierInvoiceLineResult.Duplicate);
        invoice.Status.Should().Be(CarrierInvoiceStatus.Received);
    }

    [Fact]
    public void Receive_WithOneMatchingAndOneNot_IsReceivedAndOnlyTheMatchedCostIsInvoiced()
    {
        var matched = Cost("CB-0000001", "TRX-1", 70m);
        var mismatched = Cost("CB-0000002", "TRX-2", 20m);

        var (invoice, _) = CarrierInvoice.Receive(Invoice(Line("TRX-1", 70m), Line("TRX-2", 25m)), [matched, mismatched], 0m, Now);

        invoice!.Status.Should().Be(CarrierInvoiceStatus.Received);
        invoice.Total.Should().Be(95m);
        invoice.Lines.Select(line => line.Status).Should().Equal(CarrierInvoiceLineStatus.Matched, CarrierInvoiceLineStatus.ToDecide);
        matched.Status.Should().Be(CarrierCostStatus.Invoiced);
        mismatched.Status.Should().Be(CarrierCostStatus.Open);
    }

    [Fact]
    public void Receive_WithoutLines_IsRefused()
    {
        var (invoice, refusal) = CarrierInvoice.Receive(Invoice(), [], 0m, Now);

        invoice.Should().BeNull();
        refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceWithoutLines);
        refusal.Message.Should().Contain("VDT-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Receive_ALineWithoutAReference_IsRefusedNamingTheLine(string reference)
    {
        var (invoice, refusal) = CarrierInvoice.Receive(Invoice(Line("TRX-1", 5m), Line(reference, 5m)), [], 0m, Now);

        invoice.Should().BeNull();
        refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineWithoutReference);
        refusal.Message.Should().Contain("Line 2");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Receive_AnAmountOfZeroOrLess_IsRefusedNamingTheLine(double amount)
    {
        var (invoice, refusal) = CarrierInvoice.Receive(Invoice(Line("TRX-1", (decimal)amount)), [], 0m, Now);

        invoice.Should().BeNull();
        refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineAmountNotAboveZero);
        refusal.Message.Should().Contain("Line 1");
    }

    [Fact]
    public void Receive_ARepeatedReference_IsRefusedAndInvoicesNothing()
    {
        var cost = Cost("CB-0000001", "TRX-1", 5m);

        var (invoice, refusal) = CarrierInvoice.Receive(Invoice(Line("TRX-1", 5m), Line("TRX-1", 5m)), [cost], 0m, Now);

        invoice.Should().BeNull();
        refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceReferenceRepeated);
        refusal.Message.Should().Contain("TRX-1");
        cost.Status.Should().Be(CarrierCostStatus.Open);
    }

    [Fact]
    public void Receive_ANegativeTolerance_IsABug()
    {
        var receive = () => CarrierInvoice.Receive(Invoice(Line("TRX-1", 5m)), [], -1m, Now);

        receive.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HasSameContentAs_TheSameContentInAnotherOrderAndCorrelation_IsTrue()
    {
        var data = Invoice(Line("TRX-1", 5m, "a"), Line("TRX-2", 6m));
        var (invoice, _) = CarrierInvoice.Receive(data, [], 0m, Now);

        invoice!.HasSameContentAs(Invoice(Line("TRX-2", 6m), Line("TRX-1", 5m, "a")) with { CorrelationId = "invoice-2" }).Should().BeTrue();
    }

    [Fact]
    public void HasSameContentAs_DifferentContent_IsFalse()
    {
        var (invoice, _) = CarrierInvoice.Receive(Invoice(Line("TRX-1", 5m), Line("TRX-2", 6m)), [], 0m, Now);

        invoice!.HasSameContentAs(Invoice(Line("TRX-1", 5m), Line("TRX-2", 7m))).Should().BeFalse("an amount differs");
        invoice.HasSameContentAs(Invoice(Line("TRX-1", 5m), Line("TRX-3", 6m))).Should().BeFalse("a reference differs");
        invoice.HasSameContentAs(Invoice(Line("TRX-1", 5m, "other"), Line("TRX-2", 6m))).Should().BeFalse("a description differs");
        invoice.HasSameContentAs(Invoice(Line("TRX-1", 5m))).Should().BeFalse("a line is missing");
        invoice.HasSameContentAs(Invoice(Line("TRX-1", 5m), Line("TRX-2", 6m)) with { InvoiceDate = new DateOnly(2026, 10, 20) }).Should().BeFalse("the date differs");
        invoice.HasSameContentAs(Invoice(Line("TRX-1", 5m), Line("TRX-2", 6m)) with { CarrierId = new CarrierId("CAR-0002") }).Should().BeFalse("the Carrier differs");
        invoice.HasSameContentAs(Invoice(Line("TRX-1", 5m), Line("TRX-2", 6m)) with { InvoiceNumber = "VDT-2" }).Should().BeFalse("the number differs");
    }
}
