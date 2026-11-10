using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Agents.Scripts;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.IntakeAgent.Bookings;

/// <summary>
/// A Shipper Persona pastes an email into "Fill from an email" and the booking form opens prefilled (ADR 0040, #458). The Partner API stand-in answers with the scripted fake, which knows the synthetic
/// emails of <see cref="BookingIntakeSamples"/>. The Shipper submits through the existing path, so a booking from an email travels to the TMS as any other.
/// </summary>
public sealed partial class FillFromEmailJourneyTests(IntakeAgentUiFixture norvia) : BrowserPage(norvia)
{
    [Fact]
    public async Task AGoodEmail_PrefillsTheBookingForm_AndTheShipperSubmitsItThroughTheExistingPath()
    {
        await OpenFillFromEmailAsync();

        await PasteAndFillAsync(BookingIntakeSamples.Good);

        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Book a transport", Level = 1 })).ToBeVisibleAsync();
        await Expect(Field("Booking", "Shipper reference")).ToHaveValueAsync("SH-2030-0142");
        await Expect(Field("Collection", "Street")).ToHaveValueAsync("Havenstraat 12");
        await Expect(Field("Collection", "Postcode")).ToHaveValueAsync("3011 AB");
        await Expect(Field("Collection", "City")).ToHaveValueAsync("Rotterdam");
        await Expect(Field("Collection", "Collect from")).ToHaveValueAsync("2030-11-14T08:00");
        await Expect(Field("Collection", "Collect until")).ToHaveValueAsync("2030-11-14T12:00");
        await Expect(Field("Delivery", "Consignee")).ToHaveValueAsync("Mueller GmbH");
        await Expect(Field("Delivery", "Contact name")).ToHaveValueAsync("Anna Mueller");
        await Expect(Field("Delivery", "City")).ToHaveValueAsync("Koeln");
        await Expect(Field("Goods line 1", "Quantity")).ToHaveValueAsync("2");
        await Expect(Field("Goods line 1", "Weight (kg)")).ToHaveValueAsync("850");
        // Nothing is missing, so nothing is flagged.
        await Expect(Flags()).ToHaveCountAsync(0);

        // The Shipper reviews the form and sends it as it does any booking; a reference of its own keeps this test independent of any other run.
        await Field("Booking", "Shipper reference").FillAsync(NewReference());
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Book transport" }).ClickAsync();

        await Expect(Answer()).ToContainTextAsync("Norvia accepted the booking as Transport Order", new() { Timeout = 30_000 });
        await Expect(Answer()).ToContainTextAsync(TransportOrderNumber());
    }

    [Fact]
    public async Task APartialEmail_PrefillsWhatItGives_AndFlagsWhatIsMissingWithoutInventingIt()
    {
        await OpenFillFromEmailAsync();

        await PasteAndFillAsync(BookingIntakeSamples.Partial);

        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Book a transport", Level = 1 })).ToBeVisibleAsync();
        await Expect(Flags()).ToBeVisibleAsync();
        var flags = Flags().GetByRole(AriaRole.Listitem);
        await Expect(flags.Filter(new() { HasText = "Shipper reference" })).ToContainTextAsync("The text gives no reference.");
        await Expect(flags.Filter(new() { HasText = "Collection street" })).ToContainTextAsync("The text gives no street.");
        await Expect(flags.Filter(new() { HasText = "Collect from" })).ToContainTextAsync("\"morgen\" (tomorrow) is not a date and time.");
        await Expect(flags.Filter(new() { HasText = "Consignee" })).ToContainTextAsync("The text does not name the receiver.");
        await Expect(flags.Filter(new() { HasText = "Goods line 1, weight" })).ToContainTextAsync("\"ongeveer een ton\" is ambiguous");

        // What the email gives is filled in; what it does not give is left empty, never made up.
        await Expect(Field("Collection", "City")).ToHaveValueAsync("Utrecht");
        await Expect(Field("Delivery", "City")).ToHaveValueAsync("Antwerpen");
        await Expect(Field("Goods line 1", "Quantity")).ToHaveValueAsync("3");
        await Expect(Field("Booking", "Shipper reference")).ToHaveValueAsync("");
        await Expect(Field("Collection", "Street")).ToHaveValueAsync("");
        await Expect(Field("Collection", "Collect from")).ToHaveValueAsync("");
        await Expect(Field("Delivery", "Consignee")).ToHaveValueAsync("");
        await Expect(Field("Goods line 1", "Weight (kg)")).ToHaveValueAsync("");

        // The Shipper still decides: the incomplete form is refused by the booking path as any other, and nothing is booked.
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Book transport" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Main).GetByText("Shipper reference").First).ToBeVisibleAsync();
        await Expect(Answer()).ToHaveCountAsync(0);
    }

    [GeneratedRegex("TO-[0-9]{7}")]
    private static partial Regex TransportOrderNumber();
}
