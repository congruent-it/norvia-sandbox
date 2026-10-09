using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierBookings;

/// <summary>The Carrier confirms or rejects a request in its inbox: the forms, what Norvia replied, and how the inbox shows it.</summary>
public sealed class AnswerBookingRequestTests : PortalWebContext
{
    private static readonly CarrierBookingDetails Details = new(
        new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", null, null),
        new CarrierBookingStop("Kölner Landstraße 301", "40591", "Düsseldorf", "DEU", null, null),
        null,
        []);

    private static CarrierBookingRequestView Open(string number = "CB-0000001") => new(
        Guid.CreateVersion7(), number, "SH-0000042", new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero), Details);

    private static CarrierBookingRequestView Closed(CarrierBookingRequestView request, CarrierBookingRequestStatus status) => request with
    {
        Status = status,
        ClosedAt = request.ReceivedAt.AddHours(1),
        AnswerCorrelationId = "answer-1",
        CarrierBookingReference = status == CarrierBookingRequestStatus.Confirmed ? "VDT-77812" : null,
        PlannedCollection = status == CarrierBookingRequestStatus.Confirmed ? new DateTimeOffset(2030, 10, 2, 8, 30, 0, TimeSpan.FromHours(2)) : null,
        RejectionReason = status == CarrierBookingRequestStatus.Rejected ? CarrierRejectionReason.Other : null,
        RejectionText = status == CarrierBookingRequestStatus.Rejected ? "No trucks on Friday" : null,
    };

    private IRenderedComponent<BookingRequests> RenderInbox(params CarrierBookingRequestView[] requests)
    {
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns(requests);
        return Render<BookingRequests>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"));
    }

    private void NorviaReplies(CarrierBookingRequestView after, NorviaReply? reply, bool recorded = true) =>
        Portal.AnswerCarrierBookingAsync(new PersonaId("van-dijk-transport"), "CB-0000001", Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>())
            .Returns(new AnsweredCarrierBooking(after, reply, recorded));

    private static AngleSharp.Dom.IElement Panel(IRenderedComponent<BookingRequests> page, string heading) =>
        page.Find($"section[aria-label=\"{heading}\"]");

    [Fact]
    public void Render_AnOpenRequest_OffersToConfirmOrReject()
    {
        var page = RenderInbox(Open());

        page.Find("[aria-label=\"Confirm CB-0000001\"]").TextContent.Should().Be("Confirm");
        page.Find("[aria-label=\"Reject CB-0000001\"]").TextContent.Should().Be("Reject");
    }

    [Fact]
    public void Render_AnAnsweredRequest_ShowsHowItWasAnswered_AndOffersNothing()
    {
        var page = RenderInbox(
            Closed(Open("CB-0000001"), CarrierBookingRequestStatus.Confirmed),
            Closed(Open("CB-0000002"), CarrierBookingRequestStatus.Rejected),
            Closed(Open("CB-0000003"), CarrierBookingRequestStatus.WithdrawnByNorvia));

        var rows = page.FindAll("tbody tr");
        rows[0].TextContent.Should().Contain("Confirmed, your reference VDT-77812, collecting Wed 2 Oct 2030, 08:30");
        rows[1].TextContent.Should().Contain("Rejected: Other, No trucks on Friday");
        rows[2].TextContent.Should().Contain("Withdrawn by Norvia");
        page.FindAll("[aria-label^=\"Confirm \"]").Should().BeEmpty();
    }

    [Fact]
    public void Confirm_OpensTheConfirmForm_ForThatRequest()
    {
        var page = RenderInbox(Open("CB-0000001"), Open("CB-0000002"));

        page.Find("[aria-label=\"Confirm CB-0000002\"]").Click();

        var panel = Panel(page, "Confirm CB-0000002");
        Field(panel, "Carrier booking reference").Should().NotBeNull();
        Field(panel, "Planned collection time (optional, Amsterdam time)").Should().NotBeNull();
        panel.TextContent.Should().Contain("accept the Carrier cost");
    }

    [Fact]
    public void Confirm_WithoutAReference_SaysSoNextToTheField_AndSendsNothing()
    {
        var page = RenderInbox(Open());
        page.Find("[aria-label=\"Confirm CB-0000001\"]").Click();

        Panel(page, "Confirm CB-0000001").QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Panel(page, "Confirm CB-0000001").TextContent.Should().Contain(ConfirmForm.ReferenceRequired));
        Portal.DidNotReceive().AnswerCarrierBookingAsync(Arg.Any<PersonaId>(), Arg.Any<string>(), Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Confirm_SendsTheReferenceAndThePlannedTime_AsThePersona()
    {
        var request = Open();
        NorviaReplies(Closed(request, CarrierBookingRequestStatus.Confirmed), NorviaReply.Recorded("answer-1"));
        var page = RenderInbox(request);
        page.Find("[aria-label=\"Confirm CB-0000001\"]").Click();
        var panel = Panel(page, "Confirm CB-0000001");
        Field(panel, "Carrier booking reference").Change("VDT-77812");
        Field(panel, "Planned collection time (optional, Amsterdam time)").Change("2030-10-02T08:30");

        panel.QuerySelector("form")!.Submit();

        Portal.Received(1).AnswerCarrierBookingAsync(
            new PersonaId("van-dijk-transport"),
            "CB-0000001",
            Arg.Is<CarrierAnswer>(answer => answer is CarrierAnswer.Confirmation
                && ((CarrierAnswer.Confirmation)answer).BookingReference == "VDT-77812"
                && ((CarrierAnswer.Confirmation)answer).PlannedCollection == new DateTimeOffset(2030, 10, 2, 8, 30, 0, TimeSpan.FromHours(2))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Confirm_Recorded_ShowsTheRequestConfirmed_WithNorviasId_AndClosesTheForm()
    {
        var request = Open();
        NorviaReplies(Closed(request, CarrierBookingRequestStatus.Confirmed), NorviaReply.Recorded("answer-1"));
        var page = RenderInbox(request);
        page.Find("[aria-label=\"Confirm CB-0000001\"]").Click();
        Field(Panel(page, "Confirm CB-0000001"), "Carrier booking reference").Change("VDT-77812");

        Panel(page, "Confirm CB-0000001").QuerySelector("form")!.Submit();

        page.WaitForAssertion(() =>
        {
            page.Find("[role=status]").TextContent.Should().Contain("Norvia recorded your confirmation of CB-0000001").And.Contain("answer-1");
            page.Find("tbody tr").TextContent.Should().Contain("Confirmed, your reference VDT-77812");
            page.FindAll("section").Should().BeEmpty();
        });
    }

    [Fact]
    public void Reject_OpensTheRejectForm_AndOtherNeedsItsText()
    {
        var page = RenderInbox(Open());
        page.Find("[aria-label=\"Reject CB-0000001\"]").Click();
        var panel = Panel(page, "Reject CB-0000001");
        panel.QuerySelector("fluent-select")!.Change("Other");

        Panel(page, "Reject CB-0000001").QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Panel(page, "Reject CB-0000001").TextContent.Should().Contain(RejectForm.TextRequired));
        Portal.DidNotReceive().AnswerCarrierBookingAsync(Arg.Any<PersonaId>(), Arg.Any<string>(), Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Reject_SendsTheReasonAndText()
    {
        var request = Open();
        NorviaReplies(Closed(request, CarrierBookingRequestStatus.Rejected), NorviaReply.Recorded("answer-1"));
        var page = RenderInbox(request);
        page.Find("[aria-label=\"Reject CB-0000001\"]").Click();
        var panel = Panel(page, "Reject CB-0000001");
        Field(panel, "Reason text").Change("No trucks on Friday");

        panel.QuerySelector("form")!.Submit();

        Portal.Received(1).AnswerCarrierBookingAsync(
            new PersonaId("van-dijk-transport"),
            "CB-0000001",
            Arg.Is<CarrierAnswer>(answer => answer is CarrierAnswer.Rejection
                && ((CarrierAnswer.Rejection)answer).Reason == CarrierRejectionReason.NoCapacity
                && ((CarrierAnswer.Rejection)answer).Text == "No trucks on Friday"),
            Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("Norvia recorded your rejection of CB-0000001"));
    }

    [Fact]
    public void Answer_NorviaSaysTheBookingIsNotOpen_ShowsTheRequestWithdrawnByNorvia()
    {
        var request = Open();
        NorviaReplies(Closed(request, CarrierBookingRequestStatus.WithdrawnByNorvia), NorviaReply.Refusal(CarrierAnswerResult.BookingNotOpen, "Withdrawn.", "answer-1"));
        var page = RenderInbox(request);
        page.Find("[aria-label=\"Confirm CB-0000001\"]").Click();
        Field(Panel(page, "Confirm CB-0000001"), "Carrier booking reference").Change("VDT-77812");

        Panel(page, "Confirm CB-0000001").QuerySelector("form")!.Submit();

        page.WaitForAssertion(() =>
        {
            page.Find("[role=alert]").TextContent.Should().Contain("no longer has CB-0000001 open");
            page.Find("tbody tr").TextContent.Should().Contain("Withdrawn by Norvia");
        });
    }

    [Fact]
    public void Answer_NotReceived_SaysToSendItAgain_AndKeepsTheFormWithWhatWasEntered()
    {
        var request = Open();
        NorviaReplies(request, NorviaReply.NotReceived(null));
        var page = RenderInbox(request);
        page.Find("[aria-label=\"Confirm CB-0000001\"]").Click();
        Field(Panel(page, "Confirm CB-0000001"), "Carrier booking reference").Change("VDT-77812");

        Panel(page, "Confirm CB-0000001").QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("did not receive your confirmation"));
        Field(Panel(page, "Confirm CB-0000001"), "Carrier booking reference").GetAttribute("value").Should().Be("VDT-77812");
        page.Find("tbody tr").TextContent.Should().NotContain("Confirmed");
    }

    [Fact]
    public void Answer_NorviaRefuses_SaysWhyInNorviasWords()
    {
        var request = Open();
        NorviaReplies(request, NorviaReply.Refusal(CarrierAnswerResult.NotAllowed, "This booking is not yours to answer.", "answer-1"));
        var page = RenderInbox(request);
        page.Find("[aria-label=\"Reject CB-0000001\"]").Click();

        Panel(page, "Reject CB-0000001").QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("This booking is not yours to answer."));
    }

    [Fact]
    public void Answer_WhenThePortalItselfFails_SaysSo_AndKeepsTheForm()
    {
        Portal.AnswerCarrierBookingAsync(new PersonaId("van-dijk-transport"), "CB-0000001", Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>())
            .Returns<AnsweredCarrierBooking?>(_ => throw new InvalidOperationException("The database is down."));
        var page = RenderInbox(Open());
        page.Find("[aria-label=\"Reject CB-0000001\"]").Click();

        Panel(page, "Reject CB-0000001").QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("couldn't be sent or recorded"));
        Panel(page, "Reject CB-0000001").Should().NotBeNull();
    }

    [Fact]
    public void Answer_ForARequestThatIsGone_IsNotFound()
    {
        var notFound = false;
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().OnNotFound += (_, _) => notFound = true;
        Portal.AnswerCarrierBookingAsync(new PersonaId("van-dijk-transport"), "CB-0000001", Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>())
            .Returns((AnsweredCarrierBooking?)null);
        var page = RenderInbox(Open());
        page.Find("[aria-label=\"Reject CB-0000001\"]").Click();

        Panel(page, "Reject CB-0000001").QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => notFound.Should().BeTrue());
    }

    [Fact]
    public void Cancel_ClosesTheForm_WithoutSendingAnything()
    {
        var page = RenderInbox(Open());
        page.Find("[aria-label=\"Confirm CB-0000001\"]").Click();

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Cancel").Click();

        page.FindAll("section").Should().BeEmpty();
        Portal.DidNotReceive().AnswerCarrierBookingAsync(Arg.Any<PersonaId>(), Arg.Any<string>(), Arg.Any<CarrierAnswer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Choose_AnotherRequest_ClearsTheLastNotice()
    {
        var request = Open("CB-0000001");
        NorviaReplies(Closed(request, CarrierBookingRequestStatus.Confirmed), NorviaReply.Recorded("answer-1"));
        var page = RenderInbox(request, Open("CB-0000002"));
        page.Find("[aria-label=\"Confirm CB-0000001\"]").Click();
        Field(Panel(page, "Confirm CB-0000001"), "Carrier booking reference").Change("VDT-77812");
        Panel(page, "Confirm CB-0000001").QuerySelector("form")!.Submit();
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("recorded"));

        page.Find("[aria-label=\"Reject CB-0000002\"]").Click();

        page.FindAll("[role=status]").Should().BeEmpty();
    }

    [Fact]
    public void Refresh_ARequestTheCarrierWasAnsweringThatNorviaClosed_TakesItsFormAway()
    {
        var open = Open();
        Portal.ListCarrierBookingRequestsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>())
            .Returns([open], [Closed(open, CarrierBookingRequestStatus.WithdrawnByNorvia)]);
        var page = Render<BookingRequests>(parameters => parameters.Add(p => p.PersonaId, "van-dijk-transport"));
        page.Find("[aria-label=\"Confirm CB-0000001\"]").Click();

        Time.Advance(BookingRequests.RefreshInterval);

        page.WaitForAssertion(() =>
        {
            page.FindAll("section").Should().BeEmpty();
            page.Find("tbody tr").TextContent.Should().Contain("Withdrawn by Norvia");
        });
    }
}
