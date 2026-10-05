using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierShipments;

public sealed class TrackingNoticeTests
{
    private static SentTrackingEvent Sent(TrackingReply reply, bool recorded = true) => new(
        new CarrierShipmentView("CB-0000001", "SH-0000042", "VDT-1", new CarrierBookingDetails(new CarrierBookingStop(null, null, null, null, null, null), new CarrierBookingStop(null, null, null, null, null, null), null, []),
            ShipmentStage.Booked, [], []),
        new CarrierTrackingEvent("evt-1", "VDT-1", "CB-0000001", TrackingEventType.Delivered, DateTimeOffset.UnixEpoch, "Utrecht", "3511 AW", "NLD"),
        reply,
        recorded);

    [Fact]
    public void Of_Accepted_IsSuccess_AndSaysNorviaTellsNoMore()
    {
        var notice = TrackingNotice.Of(Sent(TrackingReply.Accepted("corr-1")));

        notice.Intent.Should().Be(MessageIntent.Success);
        notice.Text.Should().Contain("Norvia accepted Delivered (POD) for CB-0000001").And.Contain("no more");
        notice.MessageId.Should().Be("corr-1");
    }

    [Fact]
    public void Of_AcceptedButNotKept_Warns() =>
        TrackingNotice.Of(Sent(TrackingReply.Accepted("corr-1"), recorded: false)).Should().Match<TrackingNotice>(
            notice => notice.Intent == MessageIntent.Warning && notice.Text.Contains("couldn't keep it"));

    [Fact]
    public void Of_NotReceived_WarnsToSendAgain() =>
        TrackingNotice.Of(Sent(TrackingReply.NotReceived(null))).Should().Match<TrackingNotice>(
            notice => notice.Intent == MessageIntent.Warning && notice.Text.Contains("Send it again") && notice.MessageId == null);

    [Fact]
    public void Of_Refused_IsAnError_InNorviasWords() =>
        TrackingNotice.Of(Sent(TrackingReply.Refused("Not a Carrier.", "corr-2"))).Should().Match<TrackingNotice>(
            notice => notice.Intent == MessageIntent.Error && notice.Text == "Not a Carrier.");
}
