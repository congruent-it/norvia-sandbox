using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierBookingRequestTrackingTests
{
    private static CarrierBookingRequest Request(bool confirmed = true)
    {
        var request = CarrierBookingRequest.Receive(
            new PersonaId("van-dijk-transport"), "CB-0000001", "SH-0000042", "release-1", new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero),
            new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Utrecht", "NLD", null, null), null, []));
        if (confirmed)
        {
            request.Answer(new CarrierAnswer.Confirmation("VDT-1"), "answer-1", new DateTimeOffset(2026, 10, 2, 9, 5, 0, TimeSpan.Zero));
        }

        return request;
    }

    [Fact]
    public void AConfirmedRequest_IsBooked_UntilItReportsProgress()
    {
        var request = Request();

        request.Stage.Should().Be(ShipmentStage.Booked);
        request.LastProgress.Should().BeNull();
    }

    [Fact]
    public void ReportTracking_Progress_MovesTheShipmentOn()
    {
        var request = Request();

        request.ReportTracking(TrackingEventType.Collected).Should().BeTrue();
        request.Stage.Should().Be(ShipmentStage.Collected);
        request.ReportTracking(TrackingEventType.Departed).Should().BeTrue();
        request.Stage.Should().Be(ShipmentStage.InTransit);
        request.ReportTracking(TrackingEventType.Delivered).Should().BeTrue();
        request.Stage.Should().Be(ShipmentStage.Delivered);
    }

    [Fact]
    public void ReportTracking_AnException_ChangesNothing()
    {
        var request = Request();

        request.ReportTracking(TrackingEventType.Damaged).Should().BeTrue();

        request.Stage.Should().Be(ShipmentStage.Booked);
        request.LastProgress.Should().BeNull();
    }

    [Fact]
    public void ReportTracking_AnEventThatIsNotValidNext_IsRefused()
    {
        var request = Request();
        request.ReportTracking(TrackingEventType.Collected);

        request.ReportTracking(TrackingEventType.CollectionFailed).Should().BeFalse();
        request.ReportTracking(TrackingEventType.Collected).Should().BeFalse();
        request.ReportTracking(TrackingEventType.Arrived).Should().BeFalse();
        request.LastProgress.Should().Be(TrackingEventType.Collected);
    }

    [Fact]
    public void ReportTracking_ForARequestThatIsNotConfirmed_IsRefused()
    {
        Request(confirmed: false).ReportTracking(TrackingEventType.Collected).Should().BeFalse();
    }
}
