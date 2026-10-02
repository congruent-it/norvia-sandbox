using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierBookings;

public sealed class NorviaReplyTests
{
    [Fact]
    public void Recorded_IsRecorded_AndSaysWhetherNorviaAlreadyHadIt()
    {
        NorviaReply.Recorded("m-1").IsRecorded.Should().BeTrue();
        NorviaReply.Recorded("m-1").Repeated.Should().BeFalse();
        NorviaReply.Recorded("m-1", repeated: true).Repeated.Should().BeTrue();
    }

    [Fact]
    public void NotReceived_IsNotRecorded_AndNeedNotHaveAMessageId()
    {
        var reply = NorviaReply.NotReceived(messageId: null);

        reply.IsRecorded.Should().BeFalse();
        reply.Result.Should().Be(CarrierAnswerResult.NotReceived);
        reply.MessageId.Should().BeNull();
    }

    [Fact]
    public void Refusal_CarriesNorviasWords()
    {
        var reply = NorviaReply.Refusal(CarrierAnswerResult.BookingNotOpen, "Norvia withdrew it.", "m-1");

        reply.Result.Should().Be(CarrierAnswerResult.BookingNotOpen);
        reply.Text.Should().Be("Norvia withdrew it.");
        reply.MessageId.Should().Be("m-1");
    }

    [Theory]
    [InlineData(CarrierAnswerResult.Recorded)]
    [InlineData(CarrierAnswerResult.NotReceived)]
    public void Refusal_ForAResultThatIsNoRefusal_Throws(CarrierAnswerResult result)
    {
        var refuse = () => NorviaReply.Refusal(result, "x", "m-1");

        refuse.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Refusal_WithoutText_Throws()
    {
        var refuse = () => NorviaReply.Refusal(CarrierAnswerResult.Invalid, " ", "m-1");

        refuse.Should().Throw<ArgumentException>();
    }
}
