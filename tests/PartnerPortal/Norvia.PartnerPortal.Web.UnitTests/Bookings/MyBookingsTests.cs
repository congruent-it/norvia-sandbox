using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

public sealed class MyBookingsTests : PortalWebContext
{
    [Fact]
    public void Render_ListsEveryBookingWithNorviasAnswer()
    {
        Portal.ListBookingsAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns(
        [
            Samples.Answered(NorviaAnswer.Accepted("TO-0000042", "corr-1"), "BD-1"),
            Samples.Answered(NorviaAnswer.Rejected([new RejectionReason("QuantityInvalid", "Goods line 1 needs a quantity of at least 1.")], "corr-2"), "BD-2"),
            Samples.Answered(NorviaAnswer.Refused("This client may not submit Transport Orders.", "corr-3"), shipperReference: null),
        ]);

        var rows = RenderPage().FindAll("tbody tr");

        rows.Should().HaveCount(3);
        rows[0].TextContent.Should().Contain("Fri 25 Sep 2026, 12:00").And.Contain("BD-1").And.Contain("Accepted").And.Contain("TO-0000042").And.Contain("corr-1");
        rows[1].TextContent.Should().Contain("Rejected").And.Contain("Goods line 1 needs a quantity of at least 1.");
        rows[2].TextContent.Should().Contain("None").And.Contain("Refused").And.Contain("This client may not submit Transport Orders.");
    }

    [Fact]
    public void Render_WithNoBookings_SaysSo_AndLinksToTheForm()
    {
        Portal.ListBookingsAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns([]);

        var page = RenderPage();

        page.Find("[role=status]").TextContent.Should().Contain("Brouwer Dranken hasn't sent Norvia a booking yet.");
        page.Find("[role=status] a").GetAttribute("href").Should().Be("/personas/brouwer-dranken/bookings/new");
    }

    [Fact]
    public void Render_WhileLoading_SaysSo()
    {
        Portal.ListBookingsAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<IReadOnlyList<BookingView>>().Task);

        RenderPage().Find("[role=status]").TextContent.Should().Contain("Loading the bookings");
    }

    [Fact]
    public void Render_WhenTheBookingsCantBeLoaded_SaysSo()
    {
        Portal.ListBookingsAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<BookingView>>(_ => throw new InvalidOperationException("The database is down."));

        RenderPage().Find("[role=alert]").TextContent.Should().Contain("The bookings couldn't be loaded.");
    }

    [Fact]
    public void Render_ForAPersonaThereIsNone_IsNotFound()
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<MyBookings>(parameters => parameters.Add(page => page.PersonaId, "nobody"));

        notFound.Should().BeTrue();
    }

    [Theory]
    [InlineData(BookingResult.AlreadyBooked, "Already booked")]
    [InlineData(BookingResult.NotReceived, "Not received")]
    public void BookingResultText_NamesEveryResultInPlainWords(BookingResult result, string text)
    {
        BookingResultText.Of(result).Should().Be(text);
    }

    private IRenderedComponent<MyBookings> RenderPage() =>
        Render<MyBookings>(parameters => parameters.Add(page => page.PersonaId, "brouwer-dranken"));
}
