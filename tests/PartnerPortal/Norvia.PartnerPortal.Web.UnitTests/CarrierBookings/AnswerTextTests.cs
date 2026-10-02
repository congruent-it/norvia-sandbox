using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierBookings;

/// <summary>The words the inbox uses for a request's state, and for what Norvia replied to an answer.</summary>
public sealed class AnswerTextTests
{
    private static readonly CarrierBookingDetails Details = new(new CarrierBookingStop(null, null, null, null, null, null), new CarrierBookingStop(null, null, null, null, null, null), null, []);

    private static readonly CarrierBookingRequestView Request = new(Guid.CreateVersion7(), "CB-0000001", "SH-0000001", Samples.SentAt, Details);

    private static readonly CarrierAnswer Confirmation = new CarrierAnswer.Confirmation("R-1");
    private static readonly CarrierAnswer Rejection = new CarrierAnswer.Rejection(CarrierRejectionReason.NoCapacity);

    [Theory]
    [InlineData(CarrierRejectionReason.NoCapacity, "No capacity")]
    [InlineData(CarrierRejectionReason.LaneNotServed, "Lane not served")]
    [InlineData(CarrierRejectionReason.WindowNotFeasible, "Window not feasible")]
    [InlineData(CarrierRejectionReason.GoodsNotAccepted, "Goods not accepted")]
    [InlineData(CarrierRejectionReason.Other, "Other")]
    public void Reason_HasItsWords(CarrierRejectionReason reason, string words) =>
        RequestStatusText.Reason(reason).Should().Be(words);

    [Fact]
    public void Of_EveryStatus_HasItsWords()
    {
        RequestStatusText.Of(Request).Should().Be("Open");
        RequestStatusText.Of(Request with { Status = CarrierBookingRequestStatus.WithdrawnByNorvia }).Should().Be("Withdrawn by Norvia");
        RequestStatusText.Of(Request with { Status = CarrierBookingRequestStatus.Confirmed, CarrierBookingReference = "R-1" }).Should().Be("Confirmed, your reference R-1");
        RequestStatusText.Of(Request with { Status = CarrierBookingRequestStatus.Rejected, RejectionReason = CarrierRejectionReason.NoCapacity }).Should().Be("Rejected: No capacity");
        RequestStatusText.Of(Request with { Status = CarrierBookingRequestStatus.Rejected }).Should().Be("Rejected: No reason");
    }

    [Fact]
    public void Notice_NothingWasSent_SaysTheRequestIsClosed()
    {
        var notice = AnswerNotice.Of("CB-0000001", Confirmation, new AnsweredCarrierBooking(Request, Reply: null));

        notice.Text.Should().Contain("no longer open");
        notice.Intent.Should().Be(MessageIntent.Info);
        notice.MessageId.Should().BeNull();
        notice.AnswerAgain.Should().BeFalse();
    }

    [Fact]
    public void Notice_Recorded_IsASuccess_ForTheKindOfAnswer()
    {
        AnswerNotice.Of("CB-0000001", Confirmation, new AnsweredCarrierBooking(Request, NorviaReply.Recorded("m-1"))).Text.Should().Contain("recorded your confirmation");
        var rejected = AnswerNotice.Of("CB-0000001", Rejection, new AnsweredCarrierBooking(Request, NorviaReply.Recorded("m-1")));
        rejected.Text.Should().Contain("recorded your rejection");
        rejected.Intent.Should().Be(MessageIntent.Success);
        rejected.MessageId.Should().Be("m-1");
    }

    [Fact]
    public void Notice_RecordedBefore_SaysNothingWasRecordedAgain()
    {
        AnswerNotice.Of("CB-0000001", Confirmation, new AnsweredCarrierBooking(Request, NorviaReply.Recorded("m-1", repeated: true))).Text
            .Should().Contain("already had your confirmation");
    }

    [Fact]
    public void Notice_RecordedButNotKept_WarnsThatTheInboxStillShowsItOpen()
    {
        var notice = AnswerNotice.Of("CB-0000001", Confirmation, new AnsweredCarrierBooking(Request, NorviaReply.Recorded("m-1"), Recorded: false));

        notice.Text.Should().Contain("couldn't keep it");
        notice.Intent.Should().Be(MessageIntent.Warning);
    }

    [Fact]
    public void Notice_NotReceived_SaysToSendItAgain_AndKeepsTheFormOpen()
    {
        var notice = AnswerNotice.Of("CB-0000001", Rejection, new AnsweredCarrierBooking(Request, NorviaReply.NotReceived(null)));

        notice.Text.Should().Contain("did not receive your rejection");
        notice.AnswerAgain.Should().BeTrue();
    }

    [Fact]
    public void Notice_BookingNotOpen_SaysNorviaWithdrewIt()
    {
        var notice = AnswerNotice.Of("CB-0000001", Confirmation, new AnsweredCarrierBooking(Request, NorviaReply.Refusal(CarrierAnswerResult.BookingNotOpen, "x", "m-1")));

        notice.Text.Should().Contain("withdrawn while you were answering").And.Contain("withdrawn by Norvia");
        notice.AnswerAgain.Should().BeFalse();
    }

    [Fact]
    public void Notice_AnyOtherRefusal_IsInNorviasWords()
    {
        var notice = AnswerNotice.Of("CB-0000001", Confirmation, new AnsweredCarrierBooking(Request, NorviaReply.Refusal(CarrierAnswerResult.NotAllowed, "Not yours.", "m-1")));

        notice.Text.Should().Be("Not yours.");
        notice.Intent.Should().Be(MessageIntent.Error);
    }

    [Fact]
    public void Notice_WithoutAnAnswerOrAnswered_Throws()
    {
        var withoutAnswer = () => AnswerNotice.Of("CB-1", null!, new AnsweredCarrierBooking(Request, null));
        var withoutAnswered = () => AnswerNotice.Of("CB-1", Confirmation, null!);

        withoutAnswer.Should().Throw<ArgumentNullException>();
        withoutAnswered.Should().Throw<ArgumentNullException>();
    }
}
