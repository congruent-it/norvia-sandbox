using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

public sealed class NewBookingTests : PortalWebContext
{
    public NewBookingTests()
    {
        Portal.DraftBookingAsync("brouwer-dranken", Arg.Any<CancellationToken>()).Returns(Samples.Draft());
    }

    [Fact]
    public void Render_FillsInTheDraft()
    {
        var page = RenderPage();

        Field(Group(page, "Booking"), "Shipper reference").GetAttribute("value").Should().Be("BK-20260925-100000");
        Field(Group(page, "Collection"), "Street").GetAttribute("value").Should().Be("Waalhaven Oostzijde 88");
        Field(Group(page, "Collection"), "Collect from").GetAttribute("value").Should().Be("2026-09-26T08:00:00");
        Field(Group(page, "Delivery"), "Consignee").GetAttribute("value").Should().Be("Café De Zwaan");
        Field(Group(page, "Delivery"), "Street").GetAttribute("value").Should().Be("Oudegracht 112");
        Field(Group(page, "Goods line 1"), "Weight (kg)").GetAttribute("value").Should().Be("850");
    }

    [Fact]
    public void Render_SaysWhomThePortalBooksAs()
    {
        RenderPage().Markup.Should().Contain("As <strong>Brouwer Dranken</strong>");
    }

    [Fact]
    public void Submit_SendsTheFormAsThePersona()
    {
        AnswerWith(NorviaAnswer.Accepted("TO-0000042", "corr-1"));
        var page = RenderPage();
        Field(Group(page, "Collection"), "City").Change("Schiedam");

        page.Find("form").Submit();

        Portal.Received(1).SubmitBookingAsync(
            "brouwer-dranken",
            Arg.Is<BookingRequest>(booking => booking.ShipperReference == "BK-20260925-100000" && booking.CollectionAddress.City == "Schiedam"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Submit_Accepted_ShowsTheTransportOrderNumber()
    {
        AnswerWith(NorviaAnswer.Accepted("TO-0000042", "corr-1"));
        var page = RenderPage();

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[aria-label=\"Norvia's answer\"]").TextContent.Should().Contain("Transport Order TO-0000042"));
    }

    [Fact]
    public void Submit_Rejected_ShowsEveryReason_AndKeepsWhatWasEntered()
    {
        AnswerWith(NorviaAnswer.Rejected([new RejectionReason("QuantityInvalid", "Goods line 1 needs a quantity of at least 1.")], "corr-1"));
        var page = RenderPage();
        Field(Group(page, "Goods line 1"), "Quantity").Change("0");

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[aria-label=\"Norvia's answer\"]").TextContent.Should().Contain("Goods line 1 needs a quantity of at least 1."));
        Field(Group(page, "Goods line 1"), "Quantity").GetAttribute("value").Should().Be("0");
        Field(Group(page, "Booking"), "Shipper reference").GetAttribute("value").Should().Be("BK-20260925-100000");
    }

    [Fact]
    public void Submit_NotReceived_SaysToSendItAgain()
    {
        AnswerWith(NorviaAnswer.NotReceived(null));
        var page = RenderPage();

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Norvia did not receive the booking"));
    }

    [Fact]
    public void Submit_WhenThePortalCantKeepTheBooking_SaysSo_AndKeepsTheForm()
    {
        Portal.SubmitBookingAsync("brouwer-dranken", Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>())
            .Returns<BookingView?>(_ => throw new InvalidOperationException("The database is down."));
        var page = RenderPage();

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("The booking couldn't be sent or recorded."));
        Field(Group(page, "Booking"), "Shipper reference").GetAttribute("value").Should().Be("BK-20260925-100000");
    }

    [Fact]
    public void Submit_WithSomethingThePartnerFormatCantCarry_SendsNothing_AndSaysWhatNextToTheField()
    {
        var page = RenderPage();
        Field(Group(page, "Delivery"), "Email").Change("not an email");

        page.Find("form").Submit();

        Portal.DidNotReceive().SubmitBookingAsync(Arg.Any<string>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>());
        Group(page, "Delivery").TextContent.Should().Contain("Enter an email address, such as name@example.com.");
    }

    [Fact]
    public void Submit_AfterFixingWhatWasWrong_ClearsTheMessage()
    {
        AnswerWith(NorviaAnswer.Accepted("TO-0000042", "corr-1"));
        var page = RenderPage();
        Field(Group(page, "Delivery"), "Email").Change("not an email");
        page.Find("form").Submit();

        Field(Group(page, "Delivery"), "Email").Change("inkoop@dezwaan.example");

        Group(page, "Delivery").TextContent.Should().NotContain("Enter an email address");
    }

    [Fact]
    public void AddAGoodsLine_AddsALine_ThatCanBeRemovedAgain()
    {
        var page = RenderPage();

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Add a goods line").Click();

        Group(page, "Goods line 2").Should().NotBeNull();
        page.Find("fluent-button[aria-label=\"Remove goods line 2\"]").Click();
        page.FindAll("legend").Select(legend => legend.TextContent.Trim()).Should().NotContain("Goods line 2");
    }

    [Fact]
    public void ASingleGoodsLine_CantBeRemoved()
    {
        RenderPage().FindAll("fluent-button[aria-label^=\"Remove goods line\"]").Should().BeEmpty();
    }

    [Fact]
    public void StartANewBooking_FillsInAFreshDraft_AndClearsTheAnswer()
    {
        AnswerWith(NorviaAnswer.Accepted("TO-0000042", "corr-1"));
        var page = RenderPage();
        page.Find("form").Submit();
        page.WaitForAssertion(() => page.FindAll("[aria-label=\"Norvia's answer\"]").Should().NotBeEmpty());
        Portal.DraftBookingAsync("brouwer-dranken", Arg.Any<CancellationToken>()).Returns(Samples.Draft() with { ShipperReference = "BK-20260925-100500" });

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Start a new booking").Click();

        page.WaitForAssertion(() => Field(Group(page, "Booking"), "Shipper reference").GetAttribute("value").Should().Be("BK-20260925-100500"));
        page.FindAll("[aria-label=\"Norvia's answer\"]").Should().BeEmpty();
    }

    [Fact]
    public void Render_ForAPersonaThereIsNone_IsNotFound()
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<NewBooking>(parameters => parameters.Add(page => page.PersonaId, "nobody"));

        notFound.Should().BeTrue();
    }

    [Fact]
    public void Render_WhenTheFormCantBeLoaded_SaysSo()
    {
        Portal.DraftBookingAsync("brouwer-dranken", Arg.Any<CancellationToken>()).Returns<BookingRequest?>(_ => throw new InvalidOperationException("The seed file is gone."));

        var page = RenderPage();

        page.Find("[role=alert]").TextContent.Should().Contain("The booking form couldn't be loaded.");
    }

    private void AnswerWith(NorviaAnswer answer) =>
        Portal.SubmitBookingAsync("brouwer-dranken", Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>()).Returns(Samples.Answered(answer));

    private IRenderedComponent<NewBooking> RenderPage() =>
        Render<NewBooking>(parameters => parameters.Add(page => page.PersonaId, "brouwer-dranken"));
}
