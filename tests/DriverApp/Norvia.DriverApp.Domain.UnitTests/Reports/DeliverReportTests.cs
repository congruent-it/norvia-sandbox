using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Domain.UnitTests.Reports;

/// <summary>A Deliver is two reports of one action (P12): the Delivered that completes the Stop, and the Proof of delivery that doesn't.</summary>
public sealed class DeliverReportTests
{
    private static readonly ReportOrigin Origin = new(new TripNumber("TR-0000012"), new DriverId("DRV-0002"), 2, new DateTimeOffset(2026, 10, 13, 11, 2, 10, TimeSpan.FromHours(2)), "corr-1");
    private static readonly StopRef Delivery = new("SH-0000031", StopKind.Delivery);

    [Fact]
    public void ADeliver_MakesADeliveredAndAProofOfDelivery_ThatShareTheActionsCorrelationIdAndTime()
    {
        var delivered = DriverReport.Deliver(new ReportId(Guid.NewGuid()), new ReportId(Guid.NewGuid()), Delivery, Origin, "Two cartons crushed");

        delivered.Delivered.Kind.Should().Be(ReportKind.Delivered);
        delivered.ProofOfDelivery.Kind.Should().Be(ReportKind.ProofOfDelivery);
        delivered.Delivered.Id.Should().NotBe(delivered.ProofOfDelivery.Id);
        foreach (var report in new[] { delivered.Delivered, delivered.ProofOfDelivery })
        {
            report.State.Should().Be(ReportState.Sending);
            report.CorrelationId.Should().Be("corr-1");
            report.EventTime.Should().Be(Origin.EventTime);
            report.Stop.Should().Be(StopKind.Delivery);
            report.ShipmentNumber.Should().Be("SH-0000031");
            report.Remark.Should().Be("Two cartons crushed");
            report.DriverId.Should().Be(Origin.DriverId);
            report.VersionHeld.Should().Be(2);
        }
    }

    [Fact]
    public void OnlyTheDelivered_CompletesTheStop()
    {
        var delivered = DriverReport.Deliver(new ReportId(Guid.NewGuid()), new ReportId(Guid.NewGuid()), Delivery, Origin, null);

        delivered.Delivered.CompletesStop.Should().BeTrue();
        delivered.ProofOfDelivery.CompletesStop.Should().BeFalse();
        delivered.Delivered.Remark.Should().BeNull();
    }

    [Fact]
    public void ACollectionStop_CannotBeDelivered()
    {
        var act = () => DriverReport.Deliver(new ReportId(Guid.NewGuid()), new ReportId(Guid.NewGuid()), new StopRef("SH-0000031", StopKind.Collection), Origin, null);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(ReportKind.Delivered)]
    [InlineData(ReportKind.ProofOfDelivery)]
    public void ADeliveredOrProofOfDelivery_IsNotAStopReportOnItsOwn(ReportKind kind)
    {
        var act = () => DriverReport.ReportStop(new ReportId(Guid.NewGuid()), kind, Delivery, Origin, null);

        act.Should().Throw<ArgumentException>();
    }
}
