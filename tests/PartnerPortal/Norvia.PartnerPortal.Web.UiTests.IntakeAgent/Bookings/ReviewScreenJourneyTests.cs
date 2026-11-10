using Microsoft.Playwright;
using Norvia.Agents.Scripts;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.IntakeAgent.Bookings;

/// <summary>
/// The booking form is the review of a Booking draft (#460): each prefilled field shows the words it came from, a flagged field holds the booking back until the Shipper confirms or edits it, a short
/// list shows the text the agent did not use, and "Start again" keeps nothing. The Partner API stand-in answers with the scripted fake, which knows the emails of <see cref="BookingIntakeSamples"/>.
/// </summary>
public sealed class ReviewScreenJourneyTests(IntakeAgentUiFixture norvia) : BrowserPage(norvia)
{
    [Fact]
    public async Task EachPrefilledField_ShowsTheWordsOfTheEmailItCameFrom()
    {
        await OpenFillFromEmailAsync();

        await PasteAndFillAsync(BookingIntakeSamples.Good);

        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Book a transport", Level = 1 })).ToBeVisibleAsync();
        await Expect(Quote("Booking", "SH-2030-0142")).ToBeVisibleAsync();
        await Expect(Quote("Collection", "Havenstraat 12")).ToBeVisibleAsync();
        await Expect(Quote("Collection", "3011 AB")).ToBeVisibleAsync();
        await Expect(Quote("Collection", "Rotterdam")).ToBeVisibleAsync();
        await Expect(Quote("Collection", "Netherlands")).ToBeVisibleAsync();
        await Expect(Quote("Delivery", "Mueller GmbH")).ToBeVisibleAsync();
        await Expect(Quote("Delivery", "+49 221 555 0100")).ToBeVisibleAsync();
        await Expect(Quote("Delivery", "Lindenallee 5")).ToBeVisibleAsync();
        await Expect(Quote("Delivery", "Koeln")).ToBeVisibleAsync();
        await Expect(Quote("Goods line 1", "850 kg in total")).ToBeVisibleAsync();
        await Expect(Quote("Goods line 1", "120x80x150 cm")).ToBeVisibleAsync();
        // The package type and the quantity both came from "2 pallets".
        await Expect(Quote("Goods line 1", "2 pallets")).ToHaveCountAsync(2);
        // Nothing is flagged, so the form says every flagged field is handled and holds nothing back.
        await Expect(Flags()).ToContainTextAsync("Every flagged field is handled.");
    }

    [Fact]
    public async Task AFlaggedField_HoldsTheBookingBack_UntilTheShipperConfirmsOrEditsIt()
    {
        await OpenFillFromEmailAsync();
        await PasteAndFillAsync(BookingIntakeSamples.Partial);
        await Expect(Flags()).ToBeVisibleAsync();

        // Sent as it is, the booking is held back and says which fields to look at; nothing is booked.
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Book transport" }).ClickAsync();
        var held = Portal.GetByRole(AriaRole.Alert).Filter(new() { HasText = "The booking is not sent yet" });
        await Expect(held).ToBeVisibleAsync();
        await Expect(held).ToContainTextAsync("Shipper reference");
        await Expect(held).ToContainTextAsync("Collection street");
        await Expect(held).ToContainTextAsync("Consignee");
        await Expect(held).ToContainTextAsync("Goods line 1, weight");
        await Expect(Answer()).ToHaveCountAsync(0);

        // Confirming the weight the agent was unsure of handles that one field and no other.
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Confirm Goods line 1, weight" }).ClickAsync();
        await Expect(Portal.GetByText("Checked", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Flags().GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Goods line 1, weight" })).ToHaveCountAsync(0);
        await Expect(Flags().GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Shipper reference" })).ToHaveCountAsync(1);

        // Editing a field handles it too, once the Shipper leaves the field.
        await Field("Booking", "Shipper reference").FillAsync(NewReference());
        await Field("Booking", "Shipper reference").BlurAsync();
        await Expect(Flags().GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Shipper reference" })).ToHaveCountAsync(0);
        await Expect(Flags().GetByRole(AriaRole.Listitem).Filter(new() { HasText = "Collection street" })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task OnceEveryFlaggedFieldIsEdited_TheBookingIsSentThroughTheExistingPath()
    {
        await OpenFillFromEmailAsync();
        await PasteAndFillAsync(BookingIntakeSamples.Partial);
        await Expect(Flags()).ToBeVisibleAsync();

        await Field("Booking", "Shipper reference").FillAsync(NewReference());
        await Field("Collection", "Street").FillAsync("Havenstraat 12");
        await Field("Collection", "Postcode").FillAsync("3511 AB");
        await Field("Collection", "Country").FillAsync("Netherlands");
        await Field("Collection", "Collect from").FillAsync("2030-11-14T08:00");
        await Field("Collection", "Collect until").FillAsync("2030-11-14T12:00");
        await Field("Delivery", "Consignee").FillAsync("Mueller GmbH");
        await Field("Delivery", "Street").FillAsync("Lindenallee 5");
        await Field("Delivery", "Postcode").FillAsync("2000");
        await Field("Delivery", "Country").FillAsync("Belgium");
        await Field("Goods line 1", "Weight (kg)").FillAsync("1000");
        await Field("Goods line 1", "Weight (kg)").BlurAsync();
        await Expect(Flags()).ToContainTextAsync("Every flagged field is handled.");

        await Portal.GetByRole(AriaRole.Button, new() { Name = "Book transport" }).ClickAsync();

        await Expect(Answer()).ToContainTextAsync("Norvia accepted the booking as Transport Order", new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task TheListOfIgnoredText_ShowsTheSignatureAndTheDisclaimer()
    {
        await OpenFillFromEmailAsync();

        await PasteAndFillAsync(BookingIntakeSamples.Good);

        var ignored = Portal.GetByRole(AriaRole.Region, new() { Name = "Text the Intake agent did not use" });
        await Expect(ignored).ToBeVisibleAsync();
        await Expect(ignored.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(2);
        await Expect(ignored).ToContainTextAsync("Thanks,");
        await Expect(ignored).ToContainTextAsync("Jo");
        await Expect(ignored).ToContainTextAsync("This email is confidential and meant only for Norvia.");
    }

    [Fact]
    public async Task StartAgain_ClearsTheDraft_AndKeepsNothing()
    {
        await OpenFillFromEmailAsync();
        await PasteAndFillAsync(BookingIntakeSamples.Good);
        await Expect(Field("Collection", "City")).ToHaveValueAsync("Rotterdam");

        await Portal.GetByRole(AriaRole.Link, new() { Name = "Start again with other text" }).ClickAsync();

        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Fill from an email", Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByRole(AriaRole.Textbox, new() { Name = "The email's text" })).ToHaveValueAsync("");

        // The draft is gone: the form the Shipper reaches next has no review, no quotes and none of the draft's values.
        await Portal.GetByRole(AriaRole.Link, new() { Name = "Fill in the form myself" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Book a transport", Level = 1 })).ToBeVisibleAsync();
        await Expect(Field("Booking", "Shipper reference")).Not.ToHaveValueAsync("SH-2030-0142");
        await Expect(Field("Delivery", "Consignee")).Not.ToHaveValueAsync("Mueller GmbH");
        await Expect(Flags()).ToHaveCountAsync(0);
        await Expect(Portal.GetByText("From the email:")).ToHaveCountAsync(0);
        await Expect(Portal.GetByRole(AriaRole.Region, new() { Name = "Text the Intake agent did not use" })).ToHaveCountAsync(0);
    }

    /// <summary>The words of the email shown under a field in <paramref name="group"/>.</summary>
    private ILocator Quote(string group, string words) =>
        Portal.GetByRole(AriaRole.Group, new() { Name = group, Exact = true }).GetByText($"From the email: “{words}”", new() { Exact = true });
}
