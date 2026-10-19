using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.UnitTests.ShipperInvoices;

public sealed class ShipperInvoiceTests
{
    private static readonly ShipperId Shipper = new("SHP-0001");
    private static readonly DateTimeOffset RunAt = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly InvoiceNumber Number = InvoiceNumber.Of(2026, 1);

    private static BillableLine Line(string number, decimal freight, decimal adjustment = 0m, string shipper = "SHP-0001", int deliveredOnDay = 17)
    {
        List<ChargeLine> chargeLines = [new(ChargeLineType.Freight, freight)];
        if (adjustment > 0m)
        {
            chargeLines.Add(new ChargeLine(ChargeLineType.MinimumChargeAdjustment, adjustment));
        }

        return BillableLine.Record(
            new BillableLineData(
                number,
                new ShipperId(shipper),
                "PO-" + number,
                "SH-" + number[3..],
                new DateTimeOffset(2026, 10, deliveredOnDay, 23, 30, 0, TimeSpan.FromHours(2)),
                new FreightCharge(freight + adjustment, chargeLines, "RC-0001", 1),
                ReleasedWithoutProofOfDelivery: false,
                "intake",
                "billing"),
            RunAt).Line!;
    }

    [Fact]
    public void Issue_OverWaitingLines_HasOneInvoiceLinePerTransportOrder()
    {
        var invoice = ShipperInvoice.Issue(Number, Shipper, [Line("TO-0000002", 50m, 10m), Line("TO-0000001", 100m)], RunAt, "corr-1");

        invoice.Lines.Select(line => line.TransportOrderNumber).Should().Equal("TO-0000001", "TO-0000002");
        var second = invoice.Lines.Single(line => line.TransportOrderNumber == "TO-0000002");
        second.ChargeLines.Should().Equal(new ChargeLine(ChargeLineType.Freight, 50m), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, 10m));
        second.Total.Should().Be(60m);
        second.ShipmentNumber.Should().Be("SH-0000002");
        second.ShipperReference.Should().Be("PO-TO-0000002");
        invoice.Id.Should().Be(Number);
        invoice.ShipperId.Should().Be(Shipper);
        invoice.Status.Should().Be(ShipperInvoiceStatus.Issued);
        invoice.CorrelationId.Should().Be("corr-1");
        invoice.IssuedAt.Should().Be(RunAt);
    }

    [Fact]
    public void Issue_SetsEveryLineToInvoicedWithTheInvoiceNumber()
    {
        var lines = new[] { Line("TO-0000001", 100m), Line("TO-0000002", 50m) };

        ShipperInvoice.Issue(Number, Shipper, lines, RunAt, "corr-1");

        lines.Should().OnlyContain(line => line.Status == BillableLineStatus.Invoiced && line.InvoiceNumber == Number);
    }

    [Fact]
    public void Issue_ComputesTheThreeTotals()
    {
        var invoice = ShipperInvoice.Issue(Number, Shipper, [Line("TO-0000001", 100m), Line("TO-0000002", 50m, 10m)], RunAt, "corr-1");

        invoice.TotalWithoutVat.Should().Be(160m);
        invoice.Vat.Should().Be(33.60m);
        invoice.TotalWithVat.Should().Be(193.60m);
    }

    [Theory]
    [InlineData("0.02", "0.00")] // 0.0042
    [InlineData("0.03", "0.01")] // 0.0063
    [InlineData("0.50", "0.11")] // 0.105 is a midpoint: half away from zero rounds it up, where banker's rounding would give 0.10
    [InlineData("10.00", "2.10")]
    [InlineData("123.45", "25.92")] // 25.9245
    [InlineData("0.00", "0.00")]
    public void VatOf_RoundsToCentsHalfAwayFromZero(string withoutVat, string expected) =>
        ShipperInvoice.VatOf(decimal.Parse(withoutVat, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Issue_WithoutLines_Throws()
    {
        var issue = () => ShipperInvoice.Issue(Number, Shipper, [], RunAt, "corr-1");

        issue.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Issue_WithALineOfAnotherShipper_Throws()
    {
        var issue = () => ShipperInvoice.Issue(Number, Shipper, [Line("TO-0000001", 10m), Line("TO-0000002", 10m, shipper: "SHP-0002")], RunAt, "corr-1");

        issue.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Issue_WithAnInvoicedLine_Throws()
    {
        var line = Line("TO-0000001", 10m);
        line.Invoice(InvoiceNumber.Of(2026, 9));

        var issue = () => ShipperInvoice.Issue(Number, Shipper, [line], RunAt, "corr-1");

        issue.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(2026, 10, 19, 8, 0, 2026, 10, 19)]
    [InlineData(2026, 10, 19, 22, 30, 2026, 10, 20)] // 00:30 on the 20th in Amsterdam (CEST, UTC+2)
    [InlineData(2026, 12, 31, 23, 30, 2027, 1, 1)] // 00:30 on New Year's Day in Amsterdam (CET, UTC+1)
    [InlineData(2026, 1, 1, 0, 30, 2026, 1, 1)]
    public void InvoiceDateOf_TheDayInAmsterdam(int year, int month, int day, int hour, int minute, int expectedYear, int expectedMonth, int expectedDay)
    {
        var runAt = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        ShipperInvoice.InvoiceDateOf(runAt).Should().Be(new DateOnly(expectedYear, expectedMonth, expectedDay));
        ShipperInvoice.InvoiceYear(runAt).Should().Be(expectedYear);
    }

    [Fact]
    public void Issue_DatesTheInvoiceByTheRunInAmsterdam()
    {
        var runAt = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);

        var invoice = ShipperInvoice.Issue(Number, Shipper, [Line("TO-0000001", 10m)], runAt, "corr-1");

        invoice.InvoiceDate.Should().Be(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void BillableLineInvoice_AnInvoicedLineAgain_Throws()
    {
        var line = Line("TO-0000001", 10m);
        line.Invoice(Number);

        var again = () => line.Invoice(InvoiceNumber.Of(2026, 2));

        again.Should().Throw<InvalidOperationException>();
        line.InvoiceNumber.Should().Be(Number);
    }

    [Fact]
    public void ShipperInvoiceLine_KeepsTheDeliveryTimeWithItsOffset()
    {
        var invoice = ShipperInvoice.Issue(Number, Shipper, [Line("TO-0000001", 10m, deliveredOnDay: 17)], RunAt, "corr-1");

        invoice.Lines[0].DeliveryTime.Should().Be(new DateTimeOffset(2026, 10, 17, 23, 30, 0, TimeSpan.FromHours(2)));
        invoice.Lines[0].DeliveryTime.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void ReportSent_AnIssuedInvoice_IsSentWithTheTime()
    {
        var invoice = ShipperInvoice.Issue(Number, Shipper, [Line("TO-0000001", 100m)], RunAt, "corr-1");
        invoice.SentAt.Should().BeNull();

        var reported = invoice.ReportSent(RunAt.AddMinutes(2));

        reported.Should().BeTrue();
        invoice.Status.Should().Be(ShipperInvoiceStatus.Sent);
        invoice.SentAt.Should().Be(RunAt.AddMinutes(2));
    }

    [Fact]
    public void ReportSent_AnInvoiceReportedAlready_ChangesNothing()
    {
        var invoice = ShipperInvoice.Issue(Number, Shipper, [Line("TO-0000001", 100m)], RunAt, "corr-1");
        invoice.ReportSent(RunAt.AddMinutes(2));

        var again = invoice.ReportSent(RunAt.AddHours(1));

        again.Should().BeFalse();
        invoice.SentAt.Should().Be(RunAt.AddMinutes(2));
    }

    [Fact]
    public void ReportSent_LeavesTheNumberLinesAndTotalsAsIssued()
    {
        var invoice = ShipperInvoice.Issue(Number, Shipper, [Line("TO-0000001", 100m)], RunAt, "corr-1");

        invoice.ReportSent(RunAt.AddMinutes(2));

        invoice.Id.Should().Be(Number);
        invoice.Lines.Should().ContainSingle();
        invoice.TotalWithoutVat.Should().Be(100m);
        invoice.TotalWithVat.Should().Be(121m);
        invoice.IssuedAt.Should().Be(RunAt);
    }

    private static ShipperInvoice Issued() => ShipperInvoice.Issue(Number, Shipper, [Line("TO-0000001", 100m)], RunAt, "corr-1");

    [Fact]
    public void ReportNotDelivered_AnIssuedInvoice_IsNotDeliveredWithTheCause()
    {
        var invoice = Issued();

        var reported = invoice.ReportNotDelivered(503, "Overloaded", RunAt.AddMinutes(2));

        reported.Should().BeTrue();
        invoice.Status.Should().Be(ShipperInvoiceStatus.NotDelivered);
        invoice.NotDeliveredAt.Should().Be(RunAt.AddMinutes(2));
        invoice.NotDeliveredStatusCode.Should().Be(503);
        invoice.NotDeliveredMessage.Should().Be("Overloaded");
        invoice.SentAt.Should().BeNull();
    }

    [Fact]
    public void ReportNotDelivered_ALongCause_IsCutTo500Characters()
    {
        var invoice = Issued();

        invoice.ReportNotDelivered(null, new string('x', 900), RunAt);

        invoice.NotDeliveredMessage.Should().HaveLength(ShipperInvoice.MaxCauseLength);
        invoice.NotDeliveredStatusCode.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ReportNotDelivered_WithoutACause_Throws(string message)
    {
        var report = () => Issued().ReportNotDelivered(500, message, RunAt);

        report.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReportNotDelivered_AnInvoiceSentAlready_IsIgnored()
    {
        var invoice = Issued();
        invoice.ReportSent(RunAt.AddMinutes(1));

        var reported = invoice.ReportNotDelivered(500, "late", RunAt.AddMinutes(2));

        reported.Should().BeFalse();
        invoice.Status.Should().Be(ShipperInvoiceStatus.Sent);
        invoice.NotDeliveredAt.Should().BeNull();
    }

    [Fact]
    public void ReportNotDelivered_AnInvoiceNotDeliveredAlready_KeepsTheFirstCause()
    {
        var invoice = Issued();
        invoice.ReportNotDelivered(500, "first", RunAt.AddMinutes(1));

        var again = invoice.ReportNotDelivered(503, "second", RunAt.AddMinutes(2));

        again.Should().BeFalse();
        invoice.NotDeliveredMessage.Should().Be("first");
    }

    [Fact]
    public void ReportSent_AnInvoiceNotDelivered_IsIgnored()
    {
        var invoice = Issued();
        invoice.ReportNotDelivered(500, "cause", RunAt.AddMinutes(1));

        var reported = invoice.ReportSent(RunAt.AddMinutes(2));

        reported.Should().BeFalse();
        invoice.Status.Should().Be(ShipperInvoiceStatus.NotDelivered);
        invoice.SentAt.Should().BeNull();
    }

    [Fact]
    public void SendAgain_ANotDeliveredInvoice_IsIssuedOnceMore_AndUnchanged()
    {
        var invoice = Issued();
        invoice.ReportNotDelivered(500, "cause", RunAt.AddMinutes(1));

        var sentAgain = invoice.SendAgain();

        sentAgain.Should().BeTrue();
        invoice.Status.Should().Be(ShipperInvoiceStatus.Issued);
        invoice.NotDeliveredAt.Should().BeNull();
        invoice.NotDeliveredStatusCode.Should().BeNull();
        invoice.NotDeliveredMessage.Should().BeNull();
        invoice.Id.Should().Be(Number);
        invoice.TotalWithVat.Should().Be(121m);
        invoice.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public void SendAgain_ThenReported_IsSentOrNotDeliveredLikeTheFirstTime()
    {
        var invoice = Issued();
        invoice.ReportNotDelivered(500, "cause", RunAt.AddMinutes(1));
        invoice.SendAgain();

        invoice.ReportSent(RunAt.AddMinutes(5)).Should().BeTrue();

        invoice.Status.Should().Be(ShipperInvoiceStatus.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SendAgain_AnInvoiceThatIsNotNotDelivered_ChangesNothing(bool sent)
    {
        var invoice = Issued();
        if (sent)
        {
            invoice.ReportSent(RunAt.AddMinutes(1));
        }

        var sentAgain = invoice.SendAgain();

        sentAgain.Should().BeFalse();
        invoice.Status.Should().Be(sent ? ShipperInvoiceStatus.Sent : ShipperInvoiceStatus.Issued);
    }
}
