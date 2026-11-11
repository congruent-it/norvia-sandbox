using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// The Planner has the assistant draft the Notify Shipper message, against the scripted fake model (#444). Draft message adds wording written from what the Shipper sees of the order to the template's words;
/// the Planner edits it and sends it through the ordinary Notify Shipper flow (P7), and the Shipper's inbox in the Partner portal shows it. The draft never names the Carrier.
/// Which Agent run the sent message's trace names is read in the integration tests, not here.
/// </summary>
public sealed class DraftShipperNotificationJourneyTests(PlannerAgentUiFixture agent) : OpenExceptionsPage(agent)
{
    private const string CarrierName = "Van Dijk Transport";
    private const string FromTheView = "Your order is";

    [Fact]
    public async Task DraftMessage_AddsWhatTheShipperSeesToTheTemplate_AndNamesNoCarrier()
    {
        var (exceptionNumber, reference) = await FailedDeliveryAsync();
        await OpenListAsync(exceptionNumber);
        var message = await OpenNotifyPanelAsync(exceptionNumber);
        var template = await message.InputValueAsync();

        await DraftMessageAsync(exceptionNumber, message, template);

        var draft = await message.InputValueAsync();
        draft.Should().Contain(template.Trim(), "the draft keeps the template's words").And.Contain(FromTheView);
        draft.Should().MatchRegex(@"latest update is .* in Rotterdam", "the draft states what the Shipper sees of the order's progress");
        draft.Should().NotContainAny([CarrierName, reference, exceptionNumber], "the Shipper is never told of the Carrier or the Exception");
    }

    [Fact]
    public async Task SendNotification_AfterThePlannerEditedTheDraft_ReachesTheShippersInboxAsEdited()
    {
        var (exceptionNumber, reference) = await FailedDeliveryAsync();
        await OpenListAsync(exceptionNumber);
        var message = await OpenNotifyPanelAsync(exceptionNumber);
        await DraftMessageAsync(exceptionNumber, message, await message.InputValueAsync());
        var marker = $"Call us on Thursday ({Guid.NewGuid():N})";

        var edited = (await message.InputValueAsync()).Replace("Hello,", "Dear Anna,", StringComparison.Ordinal);
        await message.FillAsync($"{edited}\n\n{marker}");
        await NotifyPanel(exceptionNumber).GetByRole(AriaRole.Button, new() { Name = "Send notification" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync($"The Shipper is being notified of {exceptionNumber}.");

        // The inbox refreshes itself, so the notification shows once the Shipper notification worker has sent it.
        await Page.GotoAsync(new Uri(Agent.PartnerPortal, "/personas/brouwer-dranken/notifications").ToString());
        var notification = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = marker });
        await Expect(notification).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(notification).ToContainTextAsync("Dear Anna,");
        await Expect(notification).ToContainTextAsync(FromTheView);
        await Expect(notification).Not.ToContainTextAsync(CarrierName);
        await Expect(notification).Not.ToContainTextAsync(reference);
    }

    /// <summary>A Shipment Van Dijk Transport failed to deliver, the Open Exception that opened for it, and the Carrier's booking reference the Shipper must never be told.</summary>
    private async Task<(string ExceptionNumber, string Reference)> FailedDeliveryAsync()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await RecordRotterdamTrackingAsync("DeliveryFailed", reference, DateTimeOffset.UtcNow.AddMinutes(-5));
        var exceptionNumber = await Agent.OpenExceptionOfAsync(shipmentNumber, TestContext.Current.CancellationToken);
        exceptionNumber.Should().NotBeNull("the failed delivery opens an Exception");
        return (exceptionNumber!, reference);
    }
}
