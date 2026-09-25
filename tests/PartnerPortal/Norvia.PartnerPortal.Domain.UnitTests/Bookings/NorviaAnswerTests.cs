using Norvia.PartnerPortal.Domain.Bookings;

namespace Norvia.PartnerPortal.Domain.UnitTests.Bookings;

public sealed class NorviaAnswerTests
{
    [Fact]
    public void Accepted_WithATransportOrderNumber_IsBookedUnderThatNumber()
    {
        var answer = NorviaAnswer.Accepted("TO-0000042", "corr-1");

        answer.Result.Should().Be(BookingResult.Accepted);
        answer.TransportOrderNumber.Should().Be("TO-0000042");
        answer.MessageId.Should().Be("corr-1");
        answer.IsBooked.Should().BeTrue();
    }

    [Fact]
    public void AlreadyBooked_WithTheOriginalNumber_IsBookedUnderIt()
    {
        var answer = NorviaAnswer.AlreadyBooked("TO-0000042", "corr-2");

        answer.Result.Should().Be(BookingResult.AlreadyBooked);
        answer.TransportOrderNumber.Should().Be("TO-0000042");
        answer.IsBooked.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Accepted_WithoutATransportOrderNumber_Throws(string number)
    {
        var accept = () => NorviaAnswer.Accepted(number, "corr-1");

        accept.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejected_KeepsEveryReason_AndIsNotBooked()
    {
        RejectionReason[] reasons = [new("QuantityInvalid", "Goods line 1 needs a quantity of at least 1."), new("WeightInvalid", "Goods line 1 needs a weight greater than 0 kg.")];

        var answer = NorviaAnswer.Rejected(reasons, "corr-3");

        answer.Result.Should().Be(BookingResult.Rejected);
        answer.Reasons.Should().Equal(reasons);
        answer.TransportOrderNumber.Should().BeNull();
        answer.IsBooked.Should().BeFalse();
    }

    [Fact]
    public void Rejected_WithoutReasons_Throws()
    {
        var reject = () => NorviaAnswer.Rejected([], "corr-3");

        reject.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NotReceived_IsNotBooked_AndMayHaveNoMessageId()
    {
        var answer = NorviaAnswer.NotReceived(messageId: null);

        answer.Result.Should().Be(BookingResult.NotReceived);
        answer.MessageId.Should().BeNull();
        answer.IsBooked.Should().BeFalse();
    }

    [Fact]
    public void Refused_KeepsNorviasText_AndIsNotBooked()
    {
        var answer = NorviaAnswer.Refused("This client may not submit Transport Orders.", "corr-4");

        answer.Result.Should().Be(BookingResult.Refused);
        answer.Text.Should().Be("This client may not submit Transport Orders.");
        answer.IsBooked.Should().BeFalse();
    }
}
