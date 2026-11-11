using AngleSharp.Dom;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Web.Exceptions;
using Norvia.Tms.Web.UnitTests.Exceptions;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

/// <summary>#444: the Planner's Draft message replaces the Notify Shipper box and sends nothing; the notification's trace names the Agent run only when it was sent from a draft, edited or not.</summary>
public sealed class DraftShipperNotificationTests : TmsWebContext
{
    private const string Number = "EX-0000005";
    private const string Section = "section[aria-label='Notify Shipper of EX-0000005']";
    private const string Template = "Your delivery of SHIP-REF-1 failed.";
    private static readonly AgentRun Run = new("planner-notification", "run-9");

    public DraftShipperNotificationTests()
    {
        AgentSettings.Enabled = true;
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([OpenExceptionSamples.DeliveryFailed()]);
        Queries.FindShipperNotificationTemplateAsync(Number, Arg.Any<CancellationToken>()).Returns(Template);
        Commands.NotifyShipperAsync(default!, default!, default, default).ReturnsForAnyArgs(new ExceptionAnswer.Done());
    }

    private static Suggestion Draft(string text = "Hello,\n\nYour delivery of SHIP-REF-1 failed. We have no update on a new delivery time yet.\n\nKind regards,\nNorvia Planning") => new(
        text,
        [new Evidence("The template's words.", "r1", "get_shipper_notification_template")],
        "Edit the draft, then send it through Notify Shipper yourself.",
        Confidence.Medium,
        SuggestedActionKind.NeedsAPerson,
        Run);

    private IRenderedComponent<OpenExceptions> OpenNotify()
    {
        var page = Render<OpenExceptions>(parameters => parameters.AddCascadingValue<HttpContext>(new DefaultHttpContext()));
        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.Find($"fluent-button[aria-label='Notify Shipper of {Number}']").Click();
        page.WaitForElement(Section);
        return page;
    }

    private static IElement DraftButton(IRenderedComponent<OpenExceptions> page) => page.Find($"{Section} fluent-button[aria-label='Draft message for {Number}']");

    private static void Send(IRenderedComponent<OpenExceptions> page) =>
        page.Find(Section).QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == "Send notification").Click();

    private void Answer(Suggestion suggestion) =>
        Explain.DraftNotificationAsync(new DraftShipperNotificationQuery(Number), Arg.Any<CancellationToken>()).Returns(suggestion);

    [Fact]
    public void NotifyShipper_AgentsOff_OffersNoDraft()
    {
        AgentSettings.Enabled = false;

        var page = OpenNotify();

        page.FindAll($"{Section} fluent-button[aria-label^='Draft message']").Should().BeEmpty();
    }

    [Fact]
    public void NotifyShipper_AnotherRole_OffersNoDraft()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{Norvia.Tms.Web.Roles.NorviaRole.CookieName}=customer-service";
        var page = Render<OpenExceptions>(parameters => parameters.AddCascadingValue<HttpContext>(context));
        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.Find($"fluent-button[aria-label='Notify Shipper of {Number}']").Click();
        page.WaitForElement(Section);

        page.FindAll($"{Section} fluent-button[aria-label^='Draft message']").Should().BeEmpty();
    }

    [Fact]
    public void NotifyShipper_StartsFromTheTemplate_AsToday()
    {
        var page = OpenNotify();

        page.Find($"{Section} fluent-text-area").GetAttribute("value").Should().Be(Template);
    }

    [Fact]
    public void Draft_ReplacesTheMessage_AndSendsNothing()
    {
        Answer(Draft());
        var page = OpenNotify();

        DraftButton(page).Click();

        page.WaitForAssertion(() => page.Find($"{Section} fluent-text-area").GetAttribute("value").Should().Contain("We have no update on a new delivery time yet."));
        Commands.DidNotReceiveWithAnyArgs().NotifyShipperAsync(default!, default!, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Draft_LongerThanAMessageMayBe_IsCutToTheLimit()
    {
        Answer(Draft(new string('x', 5000)));
        var page = OpenNotify();

        DraftButton(page).Click();

        page.WaitForAssertion(() => page.Find($"{Section} fluent-text-area").GetAttribute("value")!.Length.Should().Be(Norvia.Tms.Domain.ShipperNotifications.ShipperNotification.MaxMessageLength));
        page.Find(Section).TextContent.Should().Contain("has been shortened");
    }

    [Fact]
    public void Send_AfterADraftLeftAsIs_NamesTheAgentRun()
    {
        Answer(Draft());
        var page = OpenNotify();
        DraftButton(page).Click();
        page.WaitForAssertion(() => page.Find($"{Section} fluent-text-area").GetAttribute("value").Should().StartWith("Hello,"));

        Send(page);

        page.WaitForAssertion(() => Commands.Received(1).NotifyShipperAsync(Number, Arg.Is<string>(message => message.StartsWith("Hello,", StringComparison.Ordinal)), Run, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Send_AfterADraftTheyEdited_StillNamesTheAgentRun()
    {
        Answer(Draft());
        var page = OpenNotify();
        DraftButton(page).Click();
        page.WaitForAssertion(() => page.Find($"{Section} fluent-text-area").GetAttribute("value").Should().StartWith("Hello,"));
        page.Find($"{Section} fluent-text-area").Input("Your delivery failed. We will call you.");

        Send(page);

        page.WaitForAssertion(() => Commands.Received(1).NotifyShipperAsync(Number, "Your delivery failed. We will call you.", Run, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Send_WithoutADraft_NamesNoAgent()
    {
        var page = OpenNotify();

        Send(page);

        page.WaitForAssertion(() => Commands.Received(1).NotifyShipperAsync(Number, Template, null, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Draft_TheAssistantIsUnavailable_SaysSo_AndKeepsTheTemplate()
    {
        Explain.DraftNotificationAsync(new DraftShipperNotificationQuery(Number), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        var page = OpenNotify();

        DraftButton(page).Click();

        page.WaitForAssertion(() => page.Find($"{Section} [role=alert]").TextContent.Should().Contain("Assistant unavailable"));
        page.Find($"{Section} fluent-text-area").GetAttribute("value").Should().Be(Template);
        Send(page);
        page.WaitForAssertion(() => Commands.Received(1).NotifyShipperAsync(Number, Template, null, Arg.Any<CancellationToken>()));
    }
}
