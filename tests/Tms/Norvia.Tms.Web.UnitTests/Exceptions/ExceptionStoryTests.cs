using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Web.Exceptions;

namespace Norvia.Tms.Web.UnitTests.Exceptions;

public sealed class ExceptionStoryTests
{
    private static readonly DateTimeOffset Opened = new(2026, 9, 24, 9, 45, 0, TimeSpan.Zero);

    [Fact]
    public void Of_TellsTheCausesNotesNotificationsCancellationAndResolutionInTheOrderTheyHappened()
    {
        var exception = Entry() with
        {
            Causes = [new ExceptionCauseView(ExceptionCauseKind.TrackingEvent, "evt-ndl", Opened), new ExceptionCauseView(ExceptionCauseKind.TrackingEvent, "evt-ndl-2", Opened.AddHours(5))],
            Notes = [new ExceptionNoteView("Phoned Kessler", "Anna de Vries", Opened.AddHours(3))],
            Notifications = [new ShipperNotificationView("Your delivery failed.", "Anna de Vries", ShipperNotificationStatus.Sent, Opened.AddHours(1), Opened.AddHours(1), null)],
            CarrierBookingCancellations = [new CarrierBookingCancellationView("CB-0000007", "Won't make its collection", Opened.AddHours(2), false, null)],
            Outcome = ExceptionOutcome.Replanned,
            Remark = "Booked with Van Dijk",
            ResolvedAt = Opened.AddHours(6),
            Status = ExceptionStatus.Resolved,
        };

        var story = ExceptionStory.Of(exception);

        story.Select(happening => (happening.What, happening.Detail)).Should().Equal(
            ("Cause: Tracking event", "Reported as evt-ndl"),
            ("Shipper notification sent (by Anna de Vries)", "Your delivery failed."),
            ("Carrier booking CB-0000007 cancelled", "Won't make its collection"),
            ("Note by Anna de Vries", "Phoned Kessler"),
            ("Cause: Tracking event", "Reported as evt-ndl-2"),
            ("Resolved: Replanned", "Booked with Van Dijk"));
    }

    [Fact]
    public void Of_ANotificationThatWasNotDelivered_ShowsTheTechnicalCause()
    {
        var exception = Entry() with
        {
            Notifications = [new ShipperNotificationView("Your delivery failed.", "Anna de Vries", ShipperNotificationStatus.NotDelivered, Opened.AddHours(1), null, "The Shipper's endpoint answered 503.")],
        };

        ExceptionStory.Of(exception).Should().Contain(happening =>
            happening.What == "Shipper notification not delivered (by Anna de Vries)" && happening.Detail == "Your delivery failed. Not delivered: The Shipper's endpoint answered 503.");
    }

    [Fact]
    public void Of_ACancellationTheCarrierWasNotToldOf_ShowsCarrierNotToldWithTheCause()
    {
        var exception = Entry() with
        {
            CarrierBookingCancellations = [new CarrierBookingCancellationView("CB-0000007", "Won't make its collection", Opened.AddHours(2), true, "Timed out")],
        };

        ExceptionStory.Of(exception).Should().Contain(happening =>
            happening.What == "Carrier booking CB-0000007 cancelled" && happening.Detail == "Won't make its collection Carrier not told: Timed out");
    }

    [Fact]
    public void Of_AWindowThatPassedHasNoSourceEvent()
    {
        var exception = Entry() with { Causes = [new ExceptionCauseView(ExceptionCauseKind.WindowPassed, null, Opened)] };

        ExceptionStory.Of(exception).Should().ContainSingle().Which.Should().Be(new ExceptionHappening(Opened, "Cause: Delivery window passed", string.Empty));
    }

    [Fact]
    public void Of_AnOpenException_HasNoResolution()
    {
        ExceptionStory.Of(Entry()).Should().NotContain(happening => happening.What.StartsWith("Resolved", StringComparison.Ordinal));
    }

    private static ExceptionTimelineEntry Entry() => new(
        "EX-0000005", ExceptionType.DeliveryFailed, ExceptionStatus.Open, Opened, false, false, [], [], [], [], null, null, null);
}
