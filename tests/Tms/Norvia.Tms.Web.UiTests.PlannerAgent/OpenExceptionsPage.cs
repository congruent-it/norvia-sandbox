using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// A browser page on the Planner's list of Open Exceptions, and the data the Draft note journeys set up for themselves: a Shipment whose delivery failed, which opens an Exception of its own.
/// </summary>
public abstract class OpenExceptionsPage(PlannerAgentUiFixture fixture) : BrowserPage(fixture)
{
    // The assistant reads the timeline through the MCP stand-in and the model before it answers; the first call on a cold host takes longer than a page does.
    protected const float AgentTimeout = 30_000;

    protected PlannerAgentUiFixture Agent { get; } = fixture;

    /// <summary>A Shipment Van Dijk Transport collected and failed to deliver, and the Open Exception that opened for it.</summary>
    protected async Task<(string ShipmentNumber, string ExceptionNumber)> ShipmentWithFailedDeliveryAsync()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await RecordRotterdamTrackingAsync("DeliveryFailed", reference, DateTimeOffset.UtcNow.AddMinutes(-5));
        var exceptionNumber = await Agent.OpenExceptionOfAsync(shipmentNumber, TestContext.Current.CancellationToken);
        exceptionNumber.Should().NotBeNull("the failed delivery opens an Exception");
        return (shipmentNumber, exceptionNumber!);
    }

    // The list reads the role from the request while it prerenders, so the role view is chosen first and the list opened with a full page load.
    protected async Task OpenListAsync(string exceptionNumber)
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await Page.GotoAsync("/exceptions");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Open Exceptions", Level = 1 })).ToBeVisibleAsync();
        await Expect(RowOf(exceptionNumber)).ToBeVisibleAsync();
    }

    protected ILocator RowOf(string exceptionNumber) => Page.GetByRole(AriaRole.Row).Filter(new() { HasText = exceptionNumber });

    protected ILocator NotePanel(string exceptionNumber) => Page.GetByRole(AriaRole.Group, new() { Name = $"Add note to {exceptionNumber}" });

    /// <summary>Opens the Add note panel of the Exception, presses Draft note and waits until the assistant has filled the Note box.</summary>
    protected async Task<ILocator> DraftAsync(string exceptionNumber)
    {
        await RowOf(exceptionNumber).GetByRole(AriaRole.Button, new() { Name = $"Add note to {exceptionNumber}" }).ClickAsync();
        var panel = NotePanel(exceptionNumber);
        await panel.GetByRole(AriaRole.Button, new() { Name = $"Draft note for {exceptionNumber}" }).ClickAsync();
        var note = panel.GetByRole(AriaRole.Textbox, new() { Name = "Note" });
        await Expect(note).Not.ToHaveValueAsync(string.Empty, new() { Timeout = AgentTimeout });
        return note;
    }
}
