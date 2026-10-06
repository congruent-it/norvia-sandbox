using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class CarrierBookingRequestAutoProgressTests
{
    private static readonly DateTimeOffset Confirmed = new(2026, 10, 2, 9, 5, 0, TimeSpan.Zero);
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    private static CarrierBookingRequest Request(bool confirmed = true)
    {
        var request = CarrierBookingRequest.Receive(
            new PersonaId("kessler-spedition"), "CB-0000001", "SH-0000042", "release-1", Confirmed.AddMinutes(-5),
            new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Utrecht", "NLD", null, null), null, []));
        if (confirmed)
        {
            request.Answer(new CarrierAnswer.Confirmation("KES-1"), "answer-1", Confirmed);
        }

        return request;
    }

    [Fact]
    public void ScheduleAutoProgress_OnAConfirmedRequest_PutsTheFirstEventADelayAfterConfirmation()
    {
        var request = Request();

        request.ScheduleAutoProgress(Delay, Confirmed);

        request.AutoProgressDelay.Should().Be(Delay);
        request.AutoProgressDueAt.Should().Be(Confirmed + Delay);
    }

    [Fact]
    public void ScheduleAutoProgress_WithNoDelay_SchedulesNothing()
    {
        var request = Request();

        request.ScheduleAutoProgress(null, Confirmed);

        request.AutoProgressDelay.Should().BeNull();
        request.AutoProgressDueAt.Should().BeNull();
    }

    [Fact]
    public void ScheduleAutoProgress_OnARequestThatIsNotConfirmed_SchedulesNothing()
    {
        var request = Request(confirmed: false);

        request.ScheduleAutoProgress(Delay, Confirmed);

        request.AutoProgressDueAt.Should().BeNull();
    }

    [Fact]
    public void AutoProgressDue_IsTheNextEvent_OnlyOnceTheDelayHasPassed()
    {
        var request = Request();
        request.ScheduleAutoProgress(Delay, Confirmed);

        request.AutoProgressDue(Confirmed + Delay - TimeSpan.FromMilliseconds(1)).Should().BeNull();
        request.AutoProgressDue(Confirmed + Delay).Should().Be(TrackingEventType.Collected);
    }

    [Fact]
    public void AutoProgressDue_ForARequestSentByHand_IsNone() => Request().AutoProgressDue(Confirmed.AddDays(1)).Should().BeNull();

    [Fact]
    public void ReportTracking_Progress_PutsTheNextEventADelayAfterThisOne()
    {
        var request = Request();
        request.ScheduleAutoProgress(Delay, Confirmed);
        var sentAt = Confirmed + Delay;

        request.ReportTracking(TrackingEventType.Collected, sentAt);

        request.AutoProgressDueAt.Should().Be(sentAt + Delay);
        request.AutoProgressDue(sentAt + Delay).Should().Be(TrackingEventType.Departed);
    }

    [Fact]
    public void ReportTracking_Delivered_PutsTheProofOfDeliveryADelayAfterIt()
    {
        var request = Request();
        request.ScheduleAutoProgress(Delay, Confirmed);

        request.ReportTracking(TrackingEventType.Delivered, Confirmed);

        request.AutoProgressDueAt.Should().Be(Confirmed + Delay);
        request.AutoProgressDue(Confirmed + Delay).Should().BeNull("Delivered is the last Tracking event");
        request.AutoProofOfDeliveryDue(Confirmed + Delay - TimeSpan.FromMilliseconds(1)).Should().BeFalse();
        request.AutoProofOfDeliveryDue(Confirmed + Delay).Should().BeTrue();
    }

    [Fact]
    public void AutoProofOfDeliveryDue_BeforeTheShipmentIsDelivered_IsFalse()
    {
        var request = Request();
        request.ScheduleAutoProgress(Delay, Confirmed);

        request.AutoProofOfDeliveryDue(Confirmed.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void AutoProofOfDeliveryDue_ForARequestSentByHand_IsFalse()
    {
        var request = Request();
        request.ReportTracking(TrackingEventType.Delivered, Confirmed);

        request.AutoProofOfDeliveryDue(Confirmed.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void ReportAutoProofOfDelivery_EndsTheSchedule()
    {
        var request = Request();
        request.ScheduleAutoProgress(Delay, Confirmed);
        request.ReportTracking(TrackingEventType.Delivered, Confirmed);

        request.ReportAutoProofOfDelivery();

        request.AutoProgressDueAt.Should().BeNull();
        request.AutoProofOfDeliveryDue(Confirmed.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void ReportAutoProofOfDelivery_BeforeTheShipmentIsDelivered_ChangesNothing()
    {
        var request = Request();
        request.ScheduleAutoProgress(Delay, Confirmed);

        request.ReportAutoProofOfDelivery();

        request.AutoProgressDueAt.Should().Be(Confirmed + Delay);
    }

    [Fact]
    public void ReportTracking_AnException_LeavesTheSchedule()
    {
        var request = Request();
        request.ScheduleAutoProgress(Delay, Confirmed);

        request.ReportTracking(TrackingEventType.Damaged, Confirmed.AddMinutes(1));

        request.AutoProgressDueAt.Should().Be(Confirmed + Delay);
    }

    [Fact]
    public void ReportTracking_OnARequestSentByHand_SchedulesNothing()
    {
        var request = Request();

        request.ReportTracking(TrackingEventType.Collected, Confirmed);

        request.AutoProgressDueAt.Should().BeNull();
    }

    [Fact]
    public void DeferAutoProgress_PutsTheEventOff_ButOnlyWhenOneIsScheduled()
    {
        var scheduled = Request();
        scheduled.ScheduleAutoProgress(Delay, Confirmed);
        var byHand = Request();

        scheduled.DeferAutoProgress(Confirmed.AddMinutes(1));
        byHand.DeferAutoProgress(Confirmed.AddMinutes(1));

        scheduled.AutoProgressDueAt.Should().Be(Confirmed.AddMinutes(1));
        byHand.AutoProgressDueAt.Should().BeNull();
    }
}
