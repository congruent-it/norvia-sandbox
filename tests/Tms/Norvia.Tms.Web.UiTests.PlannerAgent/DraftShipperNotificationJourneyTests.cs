using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// The Planner has the assistant draft the Notify Shipper message, against the scripted fake model (#444). Draft message replaces the template's words with an adjusted wording written from the template
/// and what the Shipper sees of the order; the Planner edits it and sends it through the ordinary Notify Shipper flow (P7), and the Shipper's inbox in the Partner portal shows it. The draft never names the Carrier.
/// Which Agent run the sent message's trace names is read in the integration tests, not here.
/// </summary>
public sealed class DraftShipperNotificationJourneyTests(PlannerAgentUiFixture agent) : OpenExceptionsPage(agent)
{
    private const string CarrierName = "Van Dijk Transport";

    [Fact]
    public async Task DraftMessage_ReplacesTheTemplateWithAnAdjustedWording_NamesNoCarrier_AndSendsNothing()
    {
        var (_, exceptionNumber, reference) = await ShipmentWithFailedDeliveryAndReferenceAsync();
        await OpenListAsync(exceptionNumber);
        var message = await OpenNotifyPanelAsync(exceptionNumber);
        var template = await message.InputValueAsync();

        await DraftMessageAsync(exceptionNumber, message);

        var draft = await message.InputValueAsync();
        draft.Should().NotBe(template).And.Contain("Your order is").And.Contain(template.Trim(), "the draft keeps the template's words");
        draft.Should().NotContainAny([CarrierName, reference, exceptionNumber], "the Shipper is never told of the Carrier or the Exception");
        await Expect(NotifyPanel(exceptionNumber)).ToContainTextAsync("nothing is sent until you do");

        // The Planner leaves without sending: the Exception shows no notification.
        await OpenListAsync(exceptionNumber);
        await Expect(RowOf(exceptionNumber)).Not.ToContainTextAsync("Sent");
    }

    [Fact]
    public async Task SendNotification_AfterThePlannerEditedTheDraft_ReachesTheShippersInboxAsEdited()
    {
        var (_, exceptionNumber, reference) = await ShipmentWithFailedDeliveryAndReferenceAsync();
        await OpenListAsync(exceptionNumber);
        var message = await OpenNotifyPanelAsync(exceptionNumber);
        await DraftMessageAsync(exceptionNumber, message);
        var marker = $"Call us on Thursday ({Guid.NewGuid():N})";

        await message.FillAsync((await message.InputValueAsync()) + "\n\n" + marker);
        await NotifyPanel(exceptionNumber).GetByRole(AriaRole.Button, new() { Name = "Send notification" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"The Shipper is being notified of {exceptionNumber}.");

        // The inbox refreshes itself, so the notification shows once the Shipper notification worker has sent it.
        await Page.GotoAsync(new Uri(Agent.PartnerPortal, "/personas/brouwer-dranken/notifications").ToString());
        var notification = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = marker });
        await Expect(notification).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(notification).ToContainTextAsync(new Regex("Your order is"));
        await Expect(notification).Not.ToContainTextAsync(CarrierName);
        await Expect(notification).Not.ToContainTextAsync(reference);
    }

    private async Task<(string ShipmentNumber, string ExceptionNumber, string Reference)> ShipmentWithFailedDeliveryAndReferenceAsync()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await RecordRotterdamTrackingAsync("DeliveryFailed", reference, DateTimeOffset.UtcNow.AddMinutes(-5));
        var exceptionNumber = await Agent.OpenExceptionOfAsync(shipmentNumber, TestContext.Current.CancellationToken);
        exceptionNumber.Should().NotBeNull("the failed delivery opens an Exception");
        return (shipmentNumber, exceptionNumber!, reference);
    }
}
