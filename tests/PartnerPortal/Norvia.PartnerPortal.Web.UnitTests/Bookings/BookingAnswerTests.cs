using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

public sealed class BookingAnswerTests : PortalWebContext
{
    [Fact]
    public void Accepted_ShowsTheTransportOrderNumber()
    {
        var answer = Render(NorviaAnswer.Accepted("TO-0000042", "corr-1"));

        answer.Find("[role=status]").TextContent.Should().Contain("Norvia accepted the booking as Transport Order TO-0000042.");
    }

    [Fact]
    public void AlreadyBooked_SaysNothingNewWasBooked()
    {
        var answer = Render(NorviaAnswer.AlreadyBooked("TO-0000042", "corr-1"));

        answer.Find("[role=status]").TextContent.Should().Contain("already has this booking, as Transport Order TO-0000042").And.Contain("Nothing new was booked.");
    }

    [Fact]
    public void Rejected_ListsEveryReason()
    {
        var answer = Render(NorviaAnswer.Rejected(
            [new RejectionReason("QuantityInvalid", "Goods line 1 needs a quantity of at least 1."), new RejectionReason("WeightInvalid", "Goods line 1 needs a weight greater than 0 kg.")],
            "corr-1"));

        answer.Find("[role=alert]").TextContent.Should().Contain("Norvia rejected the booking, so nothing was booked.");
        answer.FindAll("[role=alert] li").Select(item => item.TextContent).Should().Equal(
            "Goods line 1 needs a quantity of at least 1.", "Goods line 1 needs a weight greater than 0 kg.");
    }

    [Fact]
    public void NotReceived_SaysToSendItAgain()
    {
        var answer = Render(NorviaAnswer.NotReceived(null));

        answer.Find("[role=alert]").TextContent.Should().Contain("Norvia did not receive the booking, so nothing was booked. Send it again");
    }

    [Fact]
    public void Refused_GivesNorviasReason()
    {
        var answer = Render(NorviaAnswer.Refused("This client may not submit Transport Orders.", "corr-1"));

        answer.Find("[role=alert]").TextContent.Should().Contain("Norvia refused the booking: This client may not submit Transport Orders.");
    }

    [Fact]
    public void AnAnswerWithAMessageId_ShowsIt_SoNorviaCanFindTheAttempt()
    {
        Render(NorviaAnswer.Accepted("TO-0000042", "corr-1")).Markup.Should().Contain("Norvia's reference for this attempt: <span>corr-1</span>");
    }

    [Fact]
    public void AnAnswerWithoutAMessageId_ShowsNoReference()
    {
        Render(NorviaAnswer.NotReceived(null)).Markup.Should().NotContain("Norvia's reference");
    }

    private IRenderedComponent<BookingAnswer> Render(NorviaAnswer answer) =>
        Render<BookingAnswer>(parameters => parameters.Add(component => component.Booking, Samples.Answered(answer)));
}
