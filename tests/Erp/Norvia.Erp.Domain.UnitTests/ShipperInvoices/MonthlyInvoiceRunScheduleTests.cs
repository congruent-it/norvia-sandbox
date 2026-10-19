using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Domain.UnitTests.ShipperInvoices;

public sealed class MonthlyInvoiceRunScheduleTests
{
    [Theory]
    [InlineData("2026-11-01T04:59:59Z", "2026-10-01")] // 05:59:59 in Amsterdam (CET), a second before the run
    [InlineData("2026-11-01T05:00:00Z", "2026-11-01")] // 06:00 CET
    [InlineData("2026-10-01T03:59:59Z", "2026-09-01")] // 05:59:59 CEST
    [InlineData("2026-10-01T04:00:00Z", "2026-10-01")] // 06:00 CEST
    [InlineData("2026-12-31T23:30:00Z", "2026-12-01")] // already 1 Jan 00:30 in Amsterdam, but before 06:00
    [InlineData("2027-01-01T05:00:00Z", "2027-01-01")]
    [InlineData("2026-10-19T08:00:00Z", "2026-10-01")]
    public void LatestRunMonth_IsTheLatestRunNotAfterNow(string now, string expected) =>
        MonthlyInvoiceRunSchedule.LatestRunMonth(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void DueAt_IsSixInTheMorningInAmsterdam()
    {
        MonthlyInvoiceRunSchedule.DueAt(new DateOnly(2026, 11, 1)).Should().Be(new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero));
        MonthlyInvoiceRunSchedule.DueAt(new DateOnly(2026, 10, 1)).Should().Be(new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void CutoffOf_IsMidnightAtTheStartOfThe1stInAmsterdam() =>
        MonthlyInvoiceRunSchedule.CutoffOf(new DateOnly(2027, 1, 1)).Should().Be(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero));

    [Fact]
    public void WaitingBillableLinesToInvoice_WithACutoff_TakesOnlyLinesDeliveredBeforeIt()
    {
        var cutoff = MonthlyInvoiceRunSchedule.CutoffOf(new DateOnly(2027, 1, 1));
        List<BillableLine> lines =
        [
            Line("TO-0000001", new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.FromHours(1))),
            Line("TO-0000002", new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(1))),
            Line("TO-0000003", new DateTimeOffset(2027, 1, 1, 0, 1, 0, TimeSpan.FromHours(1))),
        ];

        var taken = new WaitingBillableLinesToInvoiceSpec(new ShipperId("SHP-0001"), cutoff).Evaluate(lines);

        taken.Select(line => line.Id.TransportOrderNumber).Should().Equal("TO-0000001");
    }

    private static BillableLine Line(string number, DateTimeOffset delivered) =>
        BillableLine.Record(
            new BillableLineData(
                number, new ShipperId("SHP-0001"), "PO", "SH-0000001", delivered,
                new FreightCharge(10m, [new ChargeLine(ChargeLineType.Freight, 10m)], "RC-0001", 1), false, "intake", "billing"),
            delivered).Line!;
}
