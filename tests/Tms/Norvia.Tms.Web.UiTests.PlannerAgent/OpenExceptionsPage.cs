using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// A browser page on the Planner's list of Open Exceptions, and the data the Draft note journeys set up for themselves: a Shipment whose delivery failed, which opens an Exception of its own.
/// </summary>
public abstract class OpenExceptionsPage(PlannerAgentUiFixture fixture) : BrowserPage(fixture)
{
    protected const float AgentTimeout = PlannerAgentTimeouts.Answer;

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

    protected ILocator NotifyPanel(string exceptionNumber) => Page.GetByRole(AriaRole.Group, new() { Name = $"Notify Shipper of {exceptionNumber}" });

    /// <summary>Opens the Notify Shipper panel of the Exception, which starts from the template, and returns its message box.</summary>
    protected async Task<ILocator> OpenNotifyPanelAsync(string exceptionNumber)
    {
        await RowOf(exceptionNumber).GetByRole(AriaRole.Button, new() { Name = $"Notify Shipper of {exceptionNumber}" }).ClickAsync();
        var message = NotifyPanel(exceptionNumber).GetByRole(AriaRole.Textbox, new() { Name = "Message to the Shipper" });
        await Expect(message).Not.ToHaveValueAsync(string.Empty);
        return message;
    }

    /// <summary>Presses Draft message in the open Notify Shipper panel and waits until the assistant's draft has replaced the template the box started from.</summary>
    protected async Task DraftMessageAsync(string exceptionNumber, ILocator message, string template)
    {
        await NotifyPanel(exceptionNumber).GetByRole(AriaRole.Button, new() { Name = $"Draft message for {exceptionNumber}" }).ClickAsync();
        await Expect(message).Not.ToHaveValueAsync(template, new() { Timeout = AgentTimeout });
    }

    /// <summary>Adds the note in the open panel, waits for the page to confirm it, then opens the list again to read what was stored (this fixture slows the live list down).</summary>
    protected async Task AddNoteAndReopenAsync(string exceptionNumber)
    {
        await NotePanel(exceptionNumber).GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync(exceptionNumber);
        await OpenListAsync(exceptionNumber);
    }
}
