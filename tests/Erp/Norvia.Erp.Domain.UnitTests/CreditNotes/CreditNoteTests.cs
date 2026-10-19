using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CreditNotes;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.UnitTests.CreditNotes;

public sealed class CreditNoteTests
{
    private static readonly ShipperId Shipper = new("SHP-0001");
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly CreditNoteNumber Number = CreditNoteNumber.Of(2026, 1);

    private static ShipperInvoice Invoice()
    {
        var lines = new[] { Line("TO-0000001", 100m), Line("TO-0000002", 50m) };
        return ShipperInvoice.Issue(InvoiceNumber.Of(2026, 1), Shipper, lines, IssuedAt, "invoice-corr");
    }

    private static BillableLine Line(string number, decimal freight) =>
        BillableLine.Record(
            new BillableLineData(
                number, Shipper, "PO-" + number, "SH-" + number[3..], new DateTimeOffset(2026, 10, 17, 23, 30, 0, TimeSpan.FromHours(2)),
                new FreightCharge(freight, [new ChargeLine(ChargeLineType.Freight, freight)], "RC-0001", 1), false, "intake", "billing"),
            IssuedAt).Line!;

    private static CreditNote Issue(decimal amount = 40m, string reason = "Rate Card error", decimal alreadyCredited = 0m, string line = "TO-0000001") =>
        CreditNote.Issue(Number, Invoice(), line, amount, reason, alreadyCredited, IssuedAt, "corr-1");

    [Fact]
    public void Issue_CreditsOneLineOfTheInvoice_WithTheLinesDetails()
    {
        var note = Issue();

        note.Id.Should().Be(Number);
        note.ShipperId.Should().Be(Shipper);
        note.InvoiceNumber.Value.Should().Be("INV-2026-000001");
        note.TransportOrderNumber.Should().Be("TO-0000001");
        note.ShipperReference.Should().Be("PO-TO-0000001");
        note.ShipmentNumber.Should().Be("SH-0000001");
        note.DeliveryDate.Should().Be(new DateOnly(2026, 10, 17));
        note.Reason.Should().Be("Rate Card error");
        note.IssuedAt.Should().Be(IssuedAt);
        note.CreditNoteDate.Should().Be(new DateOnly(2026, 10, 19));
        note.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public void Issue_ComputesVatAsTwentyOnePercentOfTheCreditedAmount()
    {
        var note = Issue(40m);

        (note.AmountWithoutVat, note.Vat, note.TotalWithVat).Should().Be((40m, 8.40m, 48.40m));
    }

    [Theory]
    [InlineData("0.50", "0.11")] // 0.105 is a midpoint: half away from zero rounds it up
    [InlineData("0.02", "0.00")]
    [InlineData("0.03", "0.01")]
    [InlineData("33.33", "7.00")] // 6.9993
    public void Issue_RoundsVatToCentsHalfAwayFromZero(string amount, string expectedVat) =>
        Issue(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)).Vat
            .Should().Be(decimal.Parse(expectedVat, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Issue_TheWholeLineTotal_IsAllowed() => Issue(100m).AmountWithoutVat.Should().Be(100m);

    [Fact]
    public void Issue_WhatIsLeftAfterEarlierCreditNotes_IsAllowed() => Issue(30m, alreadyCredited: 70m).AmountWithoutVat.Should().Be(30m);

    [Fact]
    public void Issue_TrimsTheReason() => Issue(reason: "  Damaged pallet  ").Reason.Should().Be("Damaged pallet");

    [Fact]
    public void Issue_LeavesTheInvoiceAsItWas()
    {
        var invoice = Invoice();
        var before = (invoice.TotalWithoutVat, invoice.Vat, invoice.TotalWithVat, invoice.Status, invoice.Lines.Count);

        CreditNote.Issue(Number, invoice, "TO-0000001", 100m, "Rate Card error", 0m, IssuedAt, "corr-1");

        (invoice.TotalWithoutVat, invoice.Vat, invoice.TotalWithVat, invoice.Status, invoice.Lines.Count).Should().Be(before);
    }

    [Fact]
    public void Refuse_AValidCreditNote_ReturnsNothing() =>
        CreditNote.Refuse(Invoice(), "TO-0000002", 50m, "reason", 0m).Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuse_WithoutAReason_IsRefusedWithCreditNoteReasonRequired(string? reason)
    {
        var refusal = CreditNote.Refuse(Invoice(), "TO-0000001", 10m, reason, 0m);

        refusal!.Code.Should().Be(CreditNoteRefusalCode.CreditNoteReasonRequired);
        refusal.Message.Should().Be("A Credit note needs a reason.");
    }

    [Fact]
    public void Refuse_AReasonOfMoreThanFiveHundredCharacters_IsRefused() =>
        CreditNote.Refuse(Invoice(), "TO-0000001", 10m, new string('x', 501), 0m)!.Code.Should().Be(CreditNoteRefusalCode.CreditNoteReasonTooLong);

    [Fact]
    public void Refuse_AReasonOfFiveHundredCharacters_IsAllowed() =>
        CreditNote.Refuse(Invoice(), "TO-0000001", 10m, new string('x', 500), 0m).Should().BeNull();

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.005")]
    public void Refuse_AnAmountThatIsNotPositiveCents_IsRefusedWithCreditNoteAmountInvalid(string amount) =>
        CreditNote.Refuse(Invoice(), "TO-0000001", decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "reason", 0m)!
            .Code.Should().Be(CreditNoteRefusalCode.CreditNoteAmountInvalid);

    [Fact]
    public void Refuse_AboveTheLineTotal_IsRefusedWithWhatIsLeft()
    {
        var refusal = CreditNote.Refuse(Invoice(), "TO-0000001", 100.01m, "reason", 0m);

        refusal!.Code.Should().Be(CreditNoteRefusalCode.CreditNoteAboveLineTotal);
        refusal.Message.Should().Contain("100.01").And.Contain("100.00").And.Contain("TO-0000001").And.Contain("INV-2026-000001");
    }

    [Fact]
    public void Refuse_AboveTheLineTotalMinusEarlierCreditNotes_IsRefused()
    {
        var refusal = CreditNote.Refuse(Invoice(), "TO-0000001", 30.01m, "reason", 70m);

        refusal!.Code.Should().Be(CreditNoteRefusalCode.CreditNoteAboveLineTotal);
        refusal.Message.Should().Contain("30.00").And.Contain("70.00");
    }

    [Fact]
    public void Refuse_ALineTheInvoiceDoesNotHave_IsRefusedWithInvoiceLineNotFound() =>
        CreditNote.Refuse(Invoice(), "TO-0000099", 10m, "reason", 0m)!.Code.Should().Be(CreditNoteRefusalCode.InvoiceLineNotFound);

    [Fact]
    public void Issue_WhatIsRefused_Throws()
    {
        var issue = () => Issue(100.01m);

        issue.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Issue_WithoutACorrelationId_Throws()
    {
        var issue = () => CreditNote.Issue(Number, Invoice(), "TO-0000001", 10m, "reason", 0m, IssuedAt, " ");

        issue.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Issue_StartsIssued_WithoutADeliveryOutcome()
    {
        var note = Issue();

        note.Status.Should().Be(ShipperInvoiceStatus.Issued);
        note.SentAt.Should().BeNull();
        note.NotDeliveredAt.Should().BeNull();
    }

    [Fact]
    public void ReportSent_AnIssuedCreditNote_IsSentWithTheTime()
    {
        var note = Issue();

        var reported = note.ReportSent(IssuedAt.AddMinutes(1));

        reported.Should().BeTrue();
        note.Status.Should().Be(ShipperInvoiceStatus.Sent);
        note.SentAt.Should().Be(IssuedAt.AddMinutes(1));
    }

    [Fact]
    public void ReportSent_ACreditNoteReportedAlready_IsIgnored()
    {
        var note = Issue();
        note.ReportSent(IssuedAt.AddMinutes(1));

        var reported = note.ReportSent(IssuedAt.AddMinutes(2));

        reported.Should().BeFalse();
        note.SentAt.Should().Be(IssuedAt.AddMinutes(1));
    }

    [Fact]
    public void ReportNotDelivered_AnIssuedCreditNote_IsNotDeliveredWithTheCause()
    {
        var note = Issue();

        var reported = note.ReportNotDelivered(503, "Overloaded", IssuedAt.AddMinutes(2));

        reported.Should().BeTrue();
        note.Status.Should().Be(ShipperInvoiceStatus.NotDelivered);
        (note.NotDeliveredAt, note.NotDeliveredStatusCode, note.NotDeliveredMessage).Should().Be((IssuedAt.AddMinutes(2), 503, "Overloaded"));
    }

    [Fact]
    public void ReportNotDelivered_ALongCause_IsCutTo500Characters()
    {
        var note = Issue();

        note.ReportNotDelivered(null, new string('x', 900), IssuedAt);

        note.NotDeliveredMessage.Should().HaveLength(500);
        note.NotDeliveredStatusCode.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ReportNotDelivered_WithoutACause_Throws(string message)
    {
        var report = () => Issue().ReportNotDelivered(500, message, IssuedAt);

        report.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReportNotDelivered_ACreditNoteSentAlready_IsIgnored()
    {
        var note = Issue();
        note.ReportSent(IssuedAt.AddMinutes(1));

        var reported = note.ReportNotDelivered(500, "late", IssuedAt.AddMinutes(2));

        reported.Should().BeFalse();
        note.Status.Should().Be(ShipperInvoiceStatus.Sent);
        note.NotDeliveredAt.Should().BeNull();
    }

    [Fact]
    public void SendAgain_ANotDeliveredCreditNote_IsIssuedOnceMore_WithoutTheCause()
    {
        var note = Issue();
        note.ReportNotDelivered(500, "cause", IssuedAt.AddMinutes(1));

        var again = note.SendAgain();

        again.Should().BeTrue();
        note.Status.Should().Be(ShipperInvoiceStatus.Issued);
        (note.NotDeliveredAt, note.NotDeliveredStatusCode, note.NotDeliveredMessage).Should().Be((null, null, null));
        note.AmountWithoutVat.Should().Be(40m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SendAgain_ACreditNoteThatIsNotNotDelivered_IsRefused(bool sent)
    {
        var note = Issue();
        if (sent)
        {
            note.ReportSent(IssuedAt.AddMinutes(1));
        }

        note.SendAgain().Should().BeFalse();
    }
}
