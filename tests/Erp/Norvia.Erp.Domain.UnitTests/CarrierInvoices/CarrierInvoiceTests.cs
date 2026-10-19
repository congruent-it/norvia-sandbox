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

    // An invoice with a Price mismatch (TRX-1, claims 80 against 70), an Unknown booking (TRX-2) and a Matched line (TRX-3).
    private static (CarrierInvoice Invoice, CarrierCost Mismatched, CarrierCost Matched) ReceivedInvoice()
    {
        var mismatched = Cost("CB-0000001", "TRX-1", 70m);
        var matched = Cost("CB-0000003", "TRX-3", 5m);
        var (invoice, _) = CarrierInvoice.Receive(Invoice(Line("TRX-1", 80m), Line("TRX-2", 10m), Line("TRX-3", 5m)), [mismatched, matched], 0m, Now);
        return (invoice!, mismatched, matched);
    }

    private static readonly DateTimeOffset Later = Now.AddHours(2);

    [Fact]
    public void Accept_AtTheInvoicedAmount_InvoicesTheCostAndLeavesTheInvoiceReceivedWhileLinesWait()
    {
        var (invoice, cost, _) = ReceivedInvoice();

        var (approved, refusal) = invoice.Accept("TRX-1", null, null, cost, Later);

        refusal.Should().BeNull();
        approved.Should().BeFalse();
        cost.Status.Should().Be(CarrierCostStatus.Invoiced);
        invoice.Status.Should().Be(CarrierInvoiceStatus.Received);
        invoice.ApprovedAmount.Should().BeNull();
        invoice.Lines[0].Should().Match<CarrierInvoiceLine>(line => line.Status == CarrierInvoiceLineStatus.Accepted && line.AcceptedAmount == 80m && line.Remark == null);
    }

    [Fact]
    public void Accept_AtAnAdjustedAmountWithARemark_KeepsBoth()
    {
        var (invoice, cost, _) = ReceivedInvoice();

        var (_, refusal) = invoice.Accept("TRX-1", 70m, "  Agreed rate  ", cost, Later);

        refusal.Should().BeNull();
        invoice.Lines[0].Should().Match<CarrierInvoiceLine>(line => line.AcceptedAmount == 70m && line.Amount == 80m && line.Remark == "Agreed rate");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Accept_AtAnAdjustedAmountWithoutARemark_IsRefusedAndChangesNothing(string? remark)
    {
        var (invoice, cost, _) = ReceivedInvoice();

        var (approved, refusal) = invoice.Accept("TRX-1", 70m, remark, cost, Later);

        approved.Should().BeFalse();
        refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceAdjustedAmountNeedsRemark);
        cost.Status.Should().Be(CarrierCostStatus.Open);
        invoice.Lines[0].Status.Should().Be(CarrierInvoiceLineStatus.ToDecide);
    }

    [Fact]
    public void Accept_AnAdjustedAmountEqualToTheInvoicedOne_NeedsNoRemark()
    {
        var (invoice, cost, _) = ReceivedInvoice();

        var (_, refusal) = invoice.Accept("TRX-1", 80m, null, cost, Later);

        refusal.Should().BeNull();
        invoice.Lines[0].Remark.Should().BeNull();
    }

    [Fact]
    public void Accept_AnAdjustedAmountNotAboveZero_IsRefused()
    {
        var (invoice, cost, _) = ReceivedInvoice();

        invoice.Accept("TRX-1", 0m, "free", cost, Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceAdjustedAmountNotAboveZero);
    }

    [Fact]
    public void Accept_AnUnknownBooking_IsRefused_AsItHasNoCarrierCost()
    {
        var (invoice, _, _) = ReceivedInvoice();

        invoice.Accept("TRX-2", null, null, null, Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineHasNoOpenCarrierCost);
    }

    [Fact]
    public void Accept_WhenTheCarrierCostHasBeenInvoicedMeanwhile_IsRefused()
    {
        var (invoice, cost, _) = ReceivedInvoice();
        cost.MarkInvoiced();

        invoice.Accept("TRX-1", null, null, cost, Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineHasNoOpenCarrierCost);
    }

    [Fact]
    public void Accept_ACarrierCostOfAnotherCarrierOrBooking_IsRefused()
    {
        var (invoice, _, _) = ReceivedInvoice();

        invoice.Accept("TRX-1", null, null, Cost("CB-0000001", "TRX-1", 70m, "CAR-0002"), Later).Refusal!.Code
            .Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineHasNoOpenCarrierCost);
        invoice.Accept("TRX-1", null, null, Cost("CB-0000009", "TRX-1", 70m), Later).Refusal!.Code
            .Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineHasNoOpenCarrierCost);
    }

    [Fact]
    public void Accept_ADuplicateLine_IsRefused()
    {
        var invoiced = Cost("CB-0000001", "TRX-1", 70m);
        invoiced.MarkInvoiced();
        var (invoice, _) = CarrierInvoice.Receive(Invoice(Line("TRX-1", 70m)), [invoiced], 0m, Now);

        invoice!.Accept("TRX-1", null, null, invoiced, Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineHasNoOpenCarrierCost);
    }

    [Fact]
    public void Decide_AMatchedOrDecidedLine_IsRefusedAsAlreadyDecided()
    {
        var (invoice, cost, matched) = ReceivedInvoice();
        invoice.Reject("TRX-1", "wrong", Later);

        invoice.Accept("TRX-3", null, null, matched, Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineAlreadyDecided);
        invoice.Reject("TRX-1", "again", Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineAlreadyDecided);
        invoice.Accept("TRX-1", null, null, cost, Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineAlreadyDecided);
    }

    [Fact]
    public void Decide_ALineTheInvoiceDoesNotHave_IsRefused()
    {
        var (invoice, cost, _) = ReceivedInvoice();

        invoice.Accept("NOPE", null, null, cost, Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineNotFound);
        invoice.Reject("NOPE", "x", Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceLineNotFound);
    }

    [Fact]
    public void Reject_LeavesTheCarrierCostOpen_AndKeepsTheReason()
    {
        var (invoice, cost, _) = ReceivedInvoice();

        var (approved, refusal) = invoice.Reject("TRX-1", " Not our booking ", Later);

        refusal.Should().BeNull();
        approved.Should().BeFalse();
        cost.Status.Should().Be(CarrierCostStatus.Open);
        invoice.Lines[0].Should().Match<CarrierInvoiceLine>(line => line.Status == CarrierInvoiceLineStatus.Rejected && line.Remark == "Not our booking" && line.AcceptedAmount == null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Reject_WithoutAReason_IsRefused(string? reason)
    {
        var (invoice, _, _) = ReceivedInvoice();

        invoice.Reject("TRX-1", reason, Later).Refusal!.Code.Should().Be(CarrierInvoiceRefusalCode.CarrierInvoiceRejectionNeedsReason);
        invoice.Lines[0].Status.Should().Be(CarrierInvoiceLineStatus.ToDecide);
    }

    [Fact]
    public void Decide_TheLastLine_ApprovesTheInvoiceAtTheSumOfTheMatchedAndAcceptedAmounts()
    {
        var (invoice, cost, _) = ReceivedInvoice();

        invoice.Accept("TRX-1", 70m, "Agreed rate", cost, Later).Approved.Should().BeFalse();
        var (approved, _) = invoice.Reject("TRX-2", "Unknown", Later);

        approved.Should().BeTrue();
        invoice.Status.Should().Be(CarrierInvoiceStatus.Approved);
        invoice.ApprovedAmount.Should().Be(75m);
        invoice.ApprovedAt.Should().Be(Later);
    }

    [Fact]
    public void Decide_RejectingEveryLineToDecide_ApprovesForTheMatchedLinesOnly()
    {
        var (invoice, _, _) = ReceivedInvoice();

        invoice.Reject("TRX-1", "no", Later);
        invoice.Reject("TRX-2", "no", Later).Approved.Should().BeTrue();

        invoice.ApprovedAmount.Should().Be(5m);
    }

    private static CarrierInvoice Waiting(params CarrierInvoiceLineData[] lines) => CarrierInvoice.Receive(Invoice(lines), [], 0m, Now).Invoice!;

    [Fact]
    public void MatchAgain_AnUnknownBookingLineWhoseCostArrives_IsMatchedAndTheLastLineApprovesTheInvoice()
    {
        var invoice = Waiting(Line("TRX-1", 70m));
        var cost = Cost("CB-0000001", "TRX-1", 70m);

        var approved = invoice.MatchAgain(cost, 0m, Now.AddHours(1));

        approved.Should().BeTrue();
        invoice.Status.Should().Be(CarrierInvoiceStatus.Approved);
        invoice.ApprovedAmount.Should().Be(70m);
        invoice.ApprovedAt.Should().Be(Now.AddHours(1));
        invoice.Lines.Single().Should().Match<CarrierInvoiceLine>(line => line.Result == CarrierInvoiceLineResult.Matched
            && line.Status == CarrierInvoiceLineStatus.Matched && line.CarrierBookingNumber == "CB-0000001");
        cost.Status.Should().Be(CarrierCostStatus.Invoiced);
    }

    [Fact]
    public void MatchAgain_AnAmountOutsideTolerance_BecomesAPriceMismatchStillToDecide()
    {
        var invoice = Waiting(Line("TRX-1", 75m));
        var cost = Cost("CB-0000001", "TRX-1", 70m);

        invoice.MatchAgain(cost, 0m, Now).Should().BeFalse();

        invoice.Status.Should().Be(CarrierInvoiceStatus.Received);
        invoice.Lines.Single().Should().Match<CarrierInvoiceLine>(line => line.Result == CarrierInvoiceLineResult.PriceMismatch
            && line.Status == CarrierInvoiceLineStatus.ToDecide && line.CarrierBookingNumber == "CB-0000001");
        cost.Status.Should().Be(CarrierCostStatus.Open);
    }

    [Fact]
    public void MatchAgain_ACostOfAnotherCarrier_MatchesNothing()
    {
        var invoice = Waiting(Line("TRX-1", 70m));
        var cost = Cost("CB-0000001", "TRX-1", 70m, "CAR-0002");

        invoice.MatchAgain(cost, 0m, Now).Should().BeFalse();

        invoice.Lines.Single().Result.Should().Be(CarrierInvoiceLineResult.UnknownBooking);
        cost.Status.Should().Be(CarrierCostStatus.Open);
    }

    [Fact]
    public void MatchAgain_ALineFinanceAlreadyRejected_IsUntouched()
    {
        var invoice = Waiting(Line("TRX-1", 70m), Line("TRX-2", 10m));
        invoice.Reject("TRX-1", "Not ours", Now);
        var cost = Cost("CB-0000001", "TRX-1", 70m);

        invoice.MatchAgain(cost, 0m, Now).Should().BeFalse();

        invoice.Lines[0].Should().Match<CarrierInvoiceLine>(line => line.Status == CarrierInvoiceLineStatus.Rejected && line.Result == CarrierInvoiceLineResult.UnknownBooking);
        cost.Status.Should().Be(CarrierCostStatus.Open);
    }

    [Fact]
    public void MatchAgain_ALineWithAnotherReference_IsUntouched()
    {
        var invoice = Waiting(Line("TRX-2", 70m));
        var cost = Cost("CB-0000001", "TRX-1", 70m);

        invoice.MatchAgain(cost, 0m, Now).Should().BeFalse();

        invoice.Lines.Single().Result.Should().Be(CarrierInvoiceLineResult.UnknownBooking);
    }

    [Fact]
    public void MatchAgain_AnApprovedInvoice_IsUntouched()
    {
        var invoice = CarrierInvoice.Receive(Invoice(Line("TRX-1", 70m)), [Cost("CB-0000001", "TRX-1", 70m)], 0m, Now).Invoice!;

        invoice.MatchAgain(Cost("CB-0000002", "TRX-1", 70m), 0m, Now).Should().BeFalse();

        invoice.Lines.Single().CarrierBookingNumber.Should().Be("CB-0000001");
    }

    [Fact]
    public void MatchAgain_WithAnotherLineStillToDecide_DoesNotApproveTheInvoice()
    {
        var invoice = Waiting(Line("TRX-1", 70m), Line("TRX-2", 10m));

        invoice.MatchAgain(Cost("CB-0000001", "TRX-1", 70m), 0m, Now).Should().BeFalse();

        invoice.Status.Should().Be(CarrierInvoiceStatus.Received);
        invoice.Lines[0].Status.Should().Be(CarrierInvoiceLineStatus.Matched);
    }
    private static CarrierInvoice ApprovedInvoice() =>
        CarrierInvoice.Receive(Invoice(Line("TRX-1", 70m)), [Cost("CB-0000001", "TRX-1", 70m)], 0m, Now).Invoice!;

    [Fact]
    public void ReportTold_AnApprovedInvoice_RecordsTheTime_OnceOnly()
    {
        var invoice = ApprovedInvoice();

        invoice.ReportTold(Now.AddMinutes(1)).Should().BeTrue();
        invoice.ReportTold(Now.AddMinutes(2)).Should().BeFalse();

        invoice.ToldAt.Should().Be(Now.AddMinutes(1));
        invoice.NotToldAt.Should().BeNull();
    }

    [Fact]
    public void ReportTold_AnInvoiceThatIsNotApproved_IsRefused()
    {
        var invoice = CarrierInvoice.Receive(Invoice(Line("TRX-1", 80m)), [Cost("CB-0000001", "TRX-1", 70m)], 0m, Now).Invoice!;

        invoice.ReportTold(Now).Should().BeFalse();

        invoice.ToldAt.Should().BeNull();
    }

    [Fact]
    public void ReportNotDelivered_AnApprovedInvoice_FlagsItCarrierNotToldWithTheCause_OnceOnly()
    {
        var invoice = ApprovedInvoice();

        invoice.ReportNotDelivered(503, "Overloaded", Now.AddMinutes(1)).Should().BeTrue();
        invoice.ReportNotDelivered(500, "Again", Now.AddMinutes(2)).Should().BeFalse();

        invoice.NotToldAt.Should().Be(Now.AddMinutes(1));
        invoice.NotToldStatusCode.Should().Be(503);
        invoice.NotToldMessage.Should().Be("Overloaded");
        invoice.ToldAt.Should().BeNull();
    }

    [Fact]
    public void ReportNotDelivered_AnInvoiceTheCarrierWasToldAlready_IsRefused()
    {
        var invoice = ApprovedInvoice();
        invoice.ReportTold(Now);

        invoice.ReportNotDelivered(500, "late", Now.AddMinutes(1)).Should().BeFalse();

        invoice.NotToldAt.Should().BeNull();
    }

    [Fact]
    public void ReportNotDelivered_AnInvoiceThatIsNotApproved_IsRefused()
    {
        var invoice = CarrierInvoice.Receive(Invoice(Line("TRX-1", 80m)), [Cost("CB-0000001", "TRX-1", 70m)], 0m, Now).Invoice!;

        invoice.ReportNotDelivered(null, "cause", Now).Should().BeFalse();
    }

    [Fact]
    public void ReportNotDelivered_ALongCause_IsKeptToFiveHundredCharacters()
    {
        var invoice = ApprovedInvoice();

        invoice.ReportNotDelivered(null, new string('x', 501), Now);

        invoice.NotToldMessage.Should().HaveLength(CarrierInvoice.MaxCauseLength);
    }

    [Fact]
    public void ReportNotDelivered_WithoutACause_Throws() =>
        FluentActions.Invoking(() => ApprovedInvoice().ReportNotDelivered(null, " ", Now)).Should().Throw<ArgumentException>();
}
