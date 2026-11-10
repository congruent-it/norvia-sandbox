using Microsoft.Playwright;
using Norvia.Agents.Scripts;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.IntakeAgent.Bookings;

/// <summary>
/// A Shipper Persona uploads the email as a PDF in "Fill from an email" (ADR 0040, #461). The Partner API reads the text of the PDF in .NET and hands it to the scripted fake, so a text PDF of a sample email
/// gives the same prefilled form as the email pasted. A scan, a file that is no PDF and a PDF over the size limit are refused with a message, and the Shipper stays on the page.
/// </summary>
public sealed class FillFromPdfJourneyTests(IntakeAgentUiFixture norvia) : BrowserPage(norvia)
{
    // The page reads at most 1,000,000 bytes (Agents:MaxPdfBytes); one more is too large.
    private const int PageLimit = 1_000_000;

    [Fact]
    public async Task ATextPdf_PrefillsTheBookingForm_AsItsTextPasted()
    {
        await OpenFillFromEmailAsync();

        await UploadAsync("email.pdf", "application/pdf", TextPdf(BookingIntakeSamples.Good));

        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Book a transport", Level = 1 })).ToBeVisibleAsync();
        await Expect(Field("Booking", "Shipper reference")).ToHaveValueAsync("SH-2030-0142");
        await Expect(Field("Collection", "Street")).ToHaveValueAsync("Havenstraat 12");
        await Expect(Field("Collection", "City")).ToHaveValueAsync("Rotterdam");
        await Expect(Field("Delivery", "Consignee")).ToHaveValueAsync("Mueller GmbH");
        await Expect(Field("Goods line 1", "Weight (kg)")).ToHaveValueAsync("850");
        await Expect(Flags()).ToContainTextAsync("Every flagged field is handled.");
    }

    [Fact]
    public async Task AScannedPdf_ShowsThatThereIsNoTextToRead_AndTheShipperStaysOnThePage()
    {
        await OpenFillFromEmailAsync();

        await UploadAsync("scan.pdf", "application/pdf", PdfWithPages(""));

        await ExpectRefusedAsync("This PDF has no text to read; paste the text instead.");
    }

    [Fact]
    public async Task AFileThatIsNotAPdf_ShowsThatTheAssistantCannotReadIt()
    {
        await OpenFillFromEmailAsync();

        await UploadAsync("photo.pdf", "application/pdf", "%PDF-nonsense"u8.ToArray());

        await ExpectRefusedAsync("The assistant can't read that file. Upload a PDF with text in it, or paste the text.");
    }

    [Fact]
    public async Task APdfOverTheSizeLimit_ShowsThatItIsTooLarge()
    {
        await OpenFillFromEmailAsync();
        var oversize = new byte[PageLimit + 1];
        TextPdf(BookingIntakeSamples.Good).CopyTo(oversize, 0);

        await UploadAsync("big.pdf", "application/pdf", oversize);

        await ExpectRefusedAsync("That is too large for the assistant. Paste only the part that describes the booking.");
    }

    private async Task UploadAsync(string name, string mimeType, byte[] content) =>
        await Portal.GetByLabel("The email as a PDF").SetInputFilesAsync(new FilePayload { Name = name, MimeType = mimeType, Buffer = content });

    /// <summary>The message above the buttons, and nothing else changed: the Shipper is still on "Fill from an email".</summary>
    private async Task ExpectRefusedAsync(string message)
    {
        await Expect(Portal.GetByRole(AriaRole.Alert)).ToContainTextAsync(message);
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Fill from an email", Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByRole(AriaRole.Link, new() { Name = "Fill in the form myself" })).ToBeVisibleAsync();
    }

    /// <summary>A PDF that holds <paramref name="text"/> line for line, small enough for a line to fit the page, so each line reads back whole.</summary>
    private static byte[] TextPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        var y = 800d;
        foreach (var line in text.Split('\n').Where(line => line.Length > 0))
        {
            page.AddText(line, 5, new PdfPoint(20, y), font);
            y -= 12;
        }

        return builder.Build();
    }

    /// <summary>A PDF with a page per text; an empty text makes a page with nothing to read, as a scan has.</summary>
    private static byte[] PdfWithPages(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            if (text.Length > 0)
            {
                page.AddText(text, 12, new PdfPoint(25, 700), font);
            }
        }

        return builder.Build();
    }
}
