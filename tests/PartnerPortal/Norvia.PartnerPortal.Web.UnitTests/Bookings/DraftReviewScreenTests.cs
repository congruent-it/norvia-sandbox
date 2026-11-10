using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

/// <summary>The booking form that opens from a Booking draft: the quotes, the flags, the gate on sending and the text that was left out.</summary>
public sealed class DraftReviewScreenTests : PortalWebContext
{
    private static readonly PersonaId Brouwer = new("brouwer-dranken");

    public DraftReviewScreenTests()
    {
        Portal.DraftBookingAsync(Brouwer, Arg.Any<CancellationToken>()).Returns(Samples.Draft());
        Portal.SubmitBookingAsync(Brouwer, Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>())
            .Returns(Samples.Answered(NorviaAnswer.Accepted("TO-0000042", "corr-1")));
    }

    private IRenderedComponent<NewBooking> RenderDraft(params FlaggedField[] flags)
    {
        Services.GetRequiredService<BookingDraftHandoff>().Put(new BookingDraftAnswer(
            BookingDraftOutcome.Drafted,
            Samples.Draft(),
            flags,
            "corr-1",
            new Dictionary<string, string> { ["bookingRef"] = "our reference is BK-20260925-100000", ["pickupCity"] = "in Rotterdam" },
            ["Groet, Jan", "Dit bericht is vertrouwelijk"]));
        return Render<NewBooking>(parameters => parameters.Add(p => p.PersonaId, "brouwer-dranken"));
    }

    private void Sent() => Portal.Received(1).SubmitBookingAsync(Brouwer, Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>());

    private void NotSent() => Portal.DidNotReceive().SubmitBookingAsync(Arg.Any<PersonaId>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>());

    [Fact]
    public void Render_ShowsTheQuoteBesideEveryPrefilledFieldThatHasOne()
    {
        var page = RenderDraft();

        Group(page, "Booking").TextContent.Should().Contain("our reference is BK-20260925-100000");
        Group(page, "Collection").TextContent.Should().Contain("in Rotterdam");
        Group(page, "Delivery").TextContent.Should().NotContain("From the email");
    }

    [Fact]
    public void Render_AFieldTheTextDidNotGive_IsFlaggedMissing()
    {
        var page = RenderDraft(new FlaggedField("receiverEmail", "The email gives no address."));

        var note = Group(page, "Delivery").QuerySelector("[data-draft-field=receiverEmail]")!;
        note.TextContent.Should().Contain("Missing: The email gives no address.");
        page.Find("[data-testid=draft-flags]").TextContent.Should().Contain("Email (missing)");
    }

    [Fact]
    public void Render_AFieldTheAgentWasUnsureOf_IsFlaggedCheckThis()
    {
        var page = RenderDraft(new FlaggedField("items.0.weight", "The email gives 850 kg and 900 kg.", CheckThis: true));

        page.Find("[data-draft-field='items.0.weight']").TextContent.Should().Contain("Check this: The email gives 850 kg and 900 kg.");
    }

    [Fact]
    public void Submit_WithAFlaggedFieldNobodyHandled_SendsNothing_AndNamesTheField()
    {
        var page = RenderDraft(new FlaggedField("receiverEmail", "The email gives no address."));

        page.Find("form").Submit();

        NotSent();
        page.Find("[data-testid=draft-not-confirmed]").TextContent.Should().Contain("Email").And.Contain("The email gives no address.");
    }

    [Fact]
    public void Submit_WithAnInvalidBookingAndAnUnhandledFlag_ShowsBothAtOnce()
    {
        var page = RenderDraft(new FlaggedField("remarks", "The email gives no instructions."));
        Field(Group(page, "Delivery"), "Email").Change("not an email");

        page.Find("form").Submit();

        NotSent();
        page.Find("[data-testid=draft-not-confirmed]").Should().NotBeNull();
        page.Markup.Should().Contain("validation-message");
    }

    [Fact]
    public void Submit_AfterTheFlaggedFieldIsConfirmed_SendsTheBooking()
    {
        var page = RenderDraft(new FlaggedField("receiverEmail", "The email gives no address."));
        page.Find("[data-draft-field=receiverEmail] fluent-button").Click();

        page.Find("form").Submit();

        Sent();
        page.FindAll("[data-testid=draft-not-confirmed]").Should().BeEmpty();
    }

    [Fact]
    public void Submit_AfterTheFlaggedFieldIsEdited_SendsTheBooking()
    {
        var page = RenderDraft(new FlaggedField("receiverEmail", "The email gives no address."));
        Field(Group(page, "Delivery"), "Email").Change("jan@example.test");

        page.Find("form").Submit();

        Sent();
    }

    [Fact]
    public void Submit_WithNoFlags_SendsTheBooking()
    {
        var page = RenderDraft();

        page.Find("form").Submit();

        Sent();
    }

    [Fact]
    public void Render_ListsTheTextTheAssistantDidNotUse()
    {
        var page = RenderDraft();

        page.Find("[data-testid=draft-ignored]").TextContent.Should().Contain("Groet, Jan").And.Contain("Dit bericht is vertrouwelijk");
    }

    [Fact]
    public void Render_OffersToStartAgain_WithOtherText()
    {
        var page = RenderDraft();

        page.Find("[data-testid=draft-review]").ParentElement!.TextContent.Should().Contain("Start again with other text");
    }

    [Fact]
    public void Render_WithoutADraft_ShowsNoReview()
    {
        var page = Render<NewBooking>(parameters => parameters.Add(p => p.PersonaId, "brouwer-dranken"));

        page.FindAll("[data-testid=draft-review]").Should().BeEmpty();
        page.FindAll("[data-draft-field]").Should().BeEmpty();
        page.Find("form").Submit();
        Sent();
    }
}
