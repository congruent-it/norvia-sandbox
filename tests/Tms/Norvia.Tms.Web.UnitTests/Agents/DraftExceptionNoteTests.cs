using AngleSharp.Dom;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Norvia.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Web.Exceptions;
using Norvia.Tms.Web.UnitTests.Exceptions;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Agents;

/// <summary>#443: the Planner's Draft note fills the Add note box and saves nothing; the note's trace names the Agent run only when it was saved from a draft, edited or not.</summary>
public sealed class DraftExceptionNoteTests : TmsWebContext
{
    private const string Number = "EX-0000005";
    private const string Section = "section[aria-label='Add note to EX-0000005']";
    private static readonly AgentRun Run = new("planner-exception-note", "run-7");

    public DraftExceptionNoteTests()
    {
        AgentSettings.Enabled = true;
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([OpenExceptionSamples.DeliveryFailed()]);
        Commands.AddNoteAsync(default!, default!, default, default).ReturnsForAnyArgs(new ExceptionAnswer.Done());
    }

    private static Suggestion Draft(string text = "Delivery failed in Rotterdam. The Shipper has not been notified.", AgentRun? run = null) => new(
        text,
        [new Evidence("The last Tracking event is DeliveryFailed.", "r1", "get_exception_timeline")],
        "Edit the draft, then save it as a note yourself.",
        Confidence.Medium,
        SuggestedActionKind.NeedsAPerson,
        run ?? Run);

    private IRenderedComponent<OpenExceptions> OpenAddNote()
    {
        var page = Render<OpenExceptions>(parameters => parameters.AddCascadingValue<HttpContext>(new DefaultHttpContext()));
        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.Find($"fluent-button[aria-label='{Number.Insert(0, "Add note to ")}']").Click();
        page.WaitForElement(Section);
        return page;
    }

    private static IElement DraftButton(IRenderedComponent<OpenExceptions> page) => page.Find($"{Section} fluent-button[aria-label='Draft note for {Number}']");

    private static void Save(IRenderedComponent<OpenExceptions> page) =>
        page.Find(Section).QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == "Add note").Click();

    private void Answer(Suggestion suggestion) =>
        Explain.DraftNoteAsync(new DraftExceptionNoteQuery(Number), Arg.Any<CancellationToken>()).Returns(suggestion);

    [Fact]
    public void AddNote_AgentsOff_OffersNoDraft()
    {
        AgentSettings.Enabled = false;

        var page = OpenAddNote();

        page.FindAll($"{Section} fluent-button[aria-label^='Draft note']").Should().BeEmpty();
    }

    [Fact]
    public void AddNote_AnotherRole_OffersNoDraft()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{Norvia.Tms.Web.Roles.NorviaRole.CookieName}=customer-service";
        var page = Render<OpenExceptions>(parameters => parameters.AddCascadingValue<HttpContext>(context));
        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.Find($"fluent-button[aria-label='Add note to {Number}']").Click();
        page.WaitForElement(Section);

        page.FindAll($"{Section} fluent-button[aria-label^='Draft note']").Should().BeEmpty();
    }

    [Fact]
    public void Draft_FillsTheNoteBox_AndSavesNothing()
    {
        Answer(Draft());
        var page = OpenAddNote();

        DraftButton(page).Click();

        page.WaitForAssertion(() => page.Find($"{Section} fluent-text-area").GetAttribute("value").Should().Be("Delivery failed in Rotterdam. The Shipper has not been notified."));
        Commands.DidNotReceiveWithAnyArgs().AddNoteAsync(default!, default!, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Draft_LongerThanANoteMayBe_IsCutToTheLimit()
    {
        Answer(Draft(new string('x', 2500)));
        var page = OpenAddNote();

        DraftButton(page).Click();

        page.WaitForAssertion(() => page.Find($"{Section} fluent-text-area").GetAttribute("value")!.Length.Should().Be(2000));
    }

    [Fact]
    public void Save_AfterADraftLeftAsIs_NamesTheAgentRun()
    {
        Answer(Draft());
        var page = OpenAddNote();
        DraftButton(page).Click();
        page.WaitForAssertion(() => page.Find($"{Section} fluent-text-area").GetAttribute("value").Should().NotBeNullOrEmpty());

        Save(page);

        page.WaitForAssertion(() => Commands.Received(1).AddNoteAsync(Number, "Delivery failed in Rotterdam. The Shipper has not been notified.", Run, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Save_AfterADraftTheyEdited_StillNamesTheAgentRun()
    {
        Answer(Draft());
        var page = OpenAddNote();
        DraftButton(page).Click();
        page.WaitForAssertion(() => page.Find($"{Section} fluent-text-area").GetAttribute("value").Should().NotBeNullOrEmpty());
        page.Find($"{Section} fluent-text-area").Input("Phoned Kessler, redelivery Thursday");

        Save(page);

        page.WaitForAssertion(() => Commands.Received(1).AddNoteAsync(Number, "Phoned Kessler, redelivery Thursday", Run, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Save_WithoutADraft_NamesNoAgent()
    {
        var page = OpenAddNote();
        page.Find($"{Section} fluent-text-area").Input("Phoned Kessler, redelivery Thursday");

        Save(page);

        page.WaitForAssertion(() => Commands.Received(1).AddNoteAsync(Number, "Phoned Kessler, redelivery Thursday", null, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Draft_TheAssistantIsUnavailable_SaysSo_AndKeepsTheBoxForTheirOwnNote()
    {
        Explain.DraftNoteAsync(new DraftExceptionNoteQuery(Number), Arg.Any<CancellationToken>()).Returns(Result<Suggestion>.Unavailable("AgentUnavailable"));
        var page = OpenAddNote();

        DraftButton(page).Click();

        page.WaitForAssertion(() => page.Find($"{Section} [role=alert]").TextContent.Should().Contain("Assistant unavailable"));
        page.Find($"{Section} fluent-text-area").Input("My own note");
        Save(page);
        page.WaitForAssertion(() => Commands.Received(1).AddNoteAsync(Number, "My own note", null, Arg.Any<CancellationToken>()));
    }
}
