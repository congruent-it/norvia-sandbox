using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

public sealed class FillFromEmailTests : PortalWebContext
{
    private static readonly PersonaId Brouwer = new("brouwer-dranken");

    public FillFromEmailTests()
    {
        Portal.DraftBookingAsync(Brouwer, Arg.Any<CancellationToken>()).Returns(Samples.Draft());
    }

    [Fact]
    public void Fill_ADraft_OpensTheBookingFormPrefilledWithTheFlags()
    {
        var draft = new BookingDraftAnswer(
            BookingDraftOutcome.Drafted,
            new BookingRequest { ShipperReference = "SH-2030-0142" },
            [new FlaggedField("pickupFrom", "\"morgen\" is not a date.")],
            "corr-1",
            new Dictionary<string, string> { ["bookingRef"] = "Our reference is SH-2030-0142" },
            ["Groet, Jan"]);
        Portal.DraftBookingFromTextAsync(Brouwer, "the email", Arg.Any<CancellationToken>()).Returns(draft);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var page = Render<FillFromEmail>(parameters => parameters.Add(p => p.PersonaId, "brouwer-dranken"));
        Field(Group(page, "Email"), "The email's text").Change("the email");

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Fill the booking").Click();

        page.WaitForAssertion(() => navigation.Uri.Should().EndWith("/personas/brouwer-dranken/bookings/new"));
        var form = Render<NewBooking>(parameters => parameters.Add(p => p.PersonaId, "brouwer-dranken"));
        form.WaitForAssertion(() =>
        {
            Field(Group(form, "Booking"), "Shipper reference").GetAttribute("value").Should().Be("SH-2030-0142");
            form.Find("[data-testid=draft-flags]").TextContent.Should().Contain("Collect from").And.Contain("\"morgen\" is not a date.");
        });
        Portal.DidNotReceive().SubmitBookingAsync(Arg.Any<PersonaId>(), Arg.Any<BookingRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BookingDraftOutcome.Unavailable, "Assistant unavailable")]
    [InlineData(BookingDraftOutcome.MultipleBookings, "more than one booking")]
    [InlineData(BookingDraftOutcome.TooLarge, "too large")]
    public void Fill_WhenNoDraftComesBack_SaysWhy_AndStaysOnThePage(BookingDraftOutcome outcome, string expected)
    {
        Portal.DraftBookingFromTextAsync(Brouwer, "the email", Arg.Any<CancellationToken>()).Returns(BookingDraftAnswer.Refused(outcome, null));
        var page = Render<FillFromEmail>(parameters => parameters.Add(p => p.PersonaId, "brouwer-dranken"));
        Field(Group(page, "Email"), "The email's text").Change("the email");

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Fill the booking").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain(expected));
    }

    [Fact]
    public void Fill_WhenTheCallThrows_SaysAssistantUnavailable()
    {
        Portal.DraftBookingFromTextAsync(Brouwer, "the email", Arg.Any<CancellationToken>()).Returns<BookingDraftAnswer?>(_ => throw new InvalidOperationException("down"));
        var page = Render<FillFromEmail>(parameters => parameters.Add(p => p.PersonaId, "brouwer-dranken"));
        Field(Group(page, "Email"), "The email's text").Change("the email");

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Fill the booking").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Assistant unavailable"));
    }

    private static readonly byte[] PdfBytes = "%PDF-1.4 test"u8.ToArray();

    [Fact]
    public void Upload_APdf_SendsItToTheAssistant_AndOpensTheBookingFormWithTheDraft()
    {
        var draft = new BookingDraftAnswer(BookingDraftOutcome.Drafted, new BookingRequest { ShipperReference = "SH-2030-0142" }, [], "corr-1", new Dictionary<string, string>(), []);
        Portal.DraftBookingFromPdfAsync(Brouwer, Arg.Is<byte[]>(bytes => bytes.SequenceEqual(PdfBytes)), Arg.Any<CancellationToken>()).Returns(draft);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var page = Render<FillFromEmail>(parameters => parameters.Add(p => p.PersonaId, "brouwer-dranken"));

        page.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(Bunit.InputFileContent.CreateFromBinary(PdfBytes, "email.pdf", contentType: "application/pdf"));

        page.WaitForAssertion(() => navigation.Uri.Should().EndWith("/personas/brouwer-dranken/bookings/new"));
    }

    [Theory]
    [InlineData(BookingDraftOutcome.NoTextInPdf, "This PDF has no text to read; paste the text instead")]
    [InlineData(BookingDraftOutcome.UnsupportedFormat, "can't read that file")]
    [InlineData(BookingDraftOutcome.TooLarge, "too large")]
    [InlineData(BookingDraftOutcome.Unavailable, "Assistant unavailable")]
    public void Upload_WhenNoDraftComesBack_SaysWhy_AndStaysOnThePage(BookingDraftOutcome outcome, string expected)
    {
        Portal.DraftBookingFromPdfAsync(Brouwer, Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(BookingDraftAnswer.Refused(outcome, null));
        var page = Render<FillFromEmail>(parameters => parameters.Add(p => p.PersonaId, "brouwer-dranken"));

        page.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(Bunit.InputFileContent.CreateFromBinary(PdfBytes, "email.pdf", contentType: "application/pdf"));

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain(expected));
    }
}
