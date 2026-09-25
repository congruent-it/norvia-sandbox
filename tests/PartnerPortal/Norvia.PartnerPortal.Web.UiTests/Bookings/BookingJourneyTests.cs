using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.Bookings;

/// <summary>
/// A presenter plays a Persona in the Partner portal and books through the Norvia Partner API, with the TMS UI beside
/// it (P1). The journeys are one class, so they run one at a time: one takes the TMS down, which the others need up.
/// </summary>
public sealed partial class BookingJourneyTests(PartnerPortalUiFixture norvia) : BrowserPages(norvia)
{
    private readonly PartnerPortalUiFixture _norvia = norvia;

    [Fact]
    public async Task ABookingAsAShipper_ShowsTheTransportOrderNumber_AndAppearsInTheTmsAtTheSameMoment()
    {
        var reference = NewReference();
        await Tms.GotoAsync(new Uri(_norvia.TmsUi, "/transport-orders").ToString());
        await Expect(Tms.GetByText("Loading the Transport Orders waiting for planning")).ToBeHiddenAsync();

        await OpenBookingFormAsync("Brouwer Dranken");
        await FillAsync("Booking", "Shipper reference", reference);
        await BookAsync();

        var number = await AcceptedNumberAsync();
        var row = Tms.GetByRole(AriaRole.Row).Filter(new() { HasText = reference });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row).ToContainTextAsync(number);
        await Expect(row).ToContainTextAsync("Brouwer Dranken");
    }

    [Fact]
    public async Task TheSameBookingSentTwice_GetsTheSameTransportOrderNumber()
    {
        await OpenBookingFormAsync("Brouwer Dranken");
        await FillAsync("Booking", "Shipper reference", NewReference());
        await BookAsync();
        var number = await AcceptedNumberAsync();

        await BookAsync();

        await Expect(Answer()).ToContainTextAsync($"Norvia already has this booking, as Transport Order {number}. Nothing new was booked.");
    }

    [Fact]
    public async Task AnInvalidBooking_ShowsEveryReason_AndKeepsWhatWasEntered()
    {
        var reference = NewReference();
        await OpenBookingFormAsync("Brouwer Dranken");
        await FillAsync("Booking", "Shipper reference", reference);
        await FillAsync("Delivery", "Consignee", "");
        await FillAsync("Goods line 1", "Quantity", "0");

        await BookAsync();

        await Expect(Answer()).ToContainTextAsync("Norvia rejected the booking, so nothing was booked.");
        await Expect(Answer().GetByRole(AriaRole.Listitem)).ToHaveCountAsync(2);
        await Expect(Answer()).ToContainTextAsync("Consignee");
        await Expect(Answer()).ToContainTextAsync("Goods line 1 needs a quantity of at least 1.");
        await Expect(Field("Booking", "Shipper reference")).ToHaveValueAsync(reference);
        await Expect(Field("Goods line 1", "Quantity")).ToHaveValueAsync("0");
        await Expect(Field("Collection", "Street")).ToHaveValueAsync("Waalhaven Oostzijde 88");
    }

    [Fact]
    public async Task AsACarrier_BookingATransportOrder_IsRefused()
    {
        await OpenBookingFormAsync("Van Dijk Transport");

        await BookAsync();

        await Expect(Answer()).ToContainTextAsync("Norvia refused the booking: This client may not submit Transport Orders.");
    }

    [Fact]
    public async Task APersonasPageOpenedByItsAddress_IsThatPersonas_WhicheverWasSwitchedToBefore()
    {
        await OpenBookingFormAsync("Brouwer Dranken");

        await Portal.GotoAsync(new Uri(_norvia.PartnerPortal, "/personas/van-dijk-transport/bookings/new").ToString());

        await Expect(Portal.GetByRole(AriaRole.Main)).ToContainTextAsync("As Van Dijk Transport");
        await Expect(Portal.GetByRole(AriaRole.Form, new() { Name = "Persona" }).GetByRole(AriaRole.Button, new() { Name = "Van Dijk Transport" }))
            .ToHaveAttributeAsync("aria-pressed", "true");
    }

    [Fact]
    public async Task MyBookings_ListsEveryBookingSent_WithNorviasAnswer()
    {
        var accepted = NewReference();
        var rejected = NewReference();
        await OpenBookingFormAsync("Brouwer Dranken");
        await FillAsync("Booking", "Shipper reference", accepted);
        await BookAsync();
        var number = await AcceptedNumberAsync();
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Start a new booking" }).ClickAsync();
        // The answer goes as the new draft comes in; what is typed before that is replaced by the draft.
        await Expect(Answer()).ToBeHiddenAsync();
        await FillAsync("Booking", "Shipper reference", rejected);
        await FillAsync("Goods line 1", "Weight (kg)", "0");
        await BookAsync();
        await Expect(Answer()).ToContainTextAsync("Norvia rejected the booking");

        await Portal.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "My bookings" }).ClickAsync();

        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "My bookings", Level = 1 })).ToBeVisibleAsync();
        var acceptedRow = Portal.GetByRole(AriaRole.Row).Filter(new() { HasText = accepted });
        await Expect(acceptedRow).ToContainTextAsync("Accepted");
        await Expect(acceptedRow).ToContainTextAsync(number);
        var rejectedRow = Portal.GetByRole(AriaRole.Row).Filter(new() { HasText = rejected });
        await Expect(rejectedRow).ToContainTextAsync("Rejected");
        await Expect(rejectedRow).ToContainTextAsync("Goods line 1 needs a weight greater than 0 kg.");
    }

    [Fact]
    public async Task WithTheTmsDown_TheBookingIsNotReceived()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await OpenBookingFormAsync("Brouwer Dranken");
        await FillAsync("Booking", "Shipper reference", NewReference());
        await _norvia.StopTmsAsync(cancellationToken);
        try
        {
            await BookAsync();

            await Expect(Answer()).ToContainTextAsync("Norvia did not receive the booking, so nothing was booked.", new() { Timeout = 60_000 });
        }
        finally
        {
            await _norvia.StartTmsAsync(cancellationToken);
        }
    }

    // Unique per test run and short, as Norvia keeps each Shipper reference once per Shipper.
    private static string NewReference() => $"UI-{Guid.NewGuid():N}"[..20];

    private async Task OpenBookingFormAsync(string persona)
    {
        await Portal.GotoAsync(_norvia.PartnerPortal.ToString());
        await Portal.GetByRole(AriaRole.Form, new() { Name = "Persona" }).GetByRole(AriaRole.Button, new() { Name = persona }).ClickAsync();
        // The form isn't prerendered, so once its heading shows, it is interactive.
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Book a transport", Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByRole(AriaRole.Main)).ToContainTextAsync($"As {persona}");
    }

    private ILocator Field(string group, string label) =>
        Portal.GetByRole(AriaRole.Group, new() { Name = group, Exact = true }).GetByLabel(label, new() { Exact = true });

    private Task FillAsync(string group, string label, string value) => Field(group, label).FillAsync(value);

    private Task BookAsync() => Portal.GetByRole(AriaRole.Button, new() { Name = "Book transport" }).ClickAsync();

    private ILocator Answer() => Portal.GetByRole(AriaRole.Region, new() { Name = "Norvia's answer" });

    private async Task<string> AcceptedNumberAsync()
    {
        await Expect(Answer()).ToContainTextAsync("Norvia accepted the booking as Transport Order", new() { Timeout = 30_000 });
        return TransportOrderNumber().Match(await Answer().InnerTextAsync()).Value;
    }

    [GeneratedRegex("TO-[0-9]{7}")]
    private static partial Regex TransportOrderNumber();
}
