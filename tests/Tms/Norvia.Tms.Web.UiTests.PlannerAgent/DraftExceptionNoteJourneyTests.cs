using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// The Planner has the assistant draft a note on an Open Exception, against the scripted fake model (#443). Draft note only fills the Add note box from the Exception's timeline: nothing is saved
/// until the Planner adds the note, and the draft chooses no outcome. Which Agent run the saved note's trace names is read in the integration tests, not here.
/// </summary>
public sealed class DraftExceptionNoteJourneyTests(PlannerAgentUiFixture agent) : OpenExceptionsPage(agent)
{
    private static readonly string[] OutcomeNames =
        ["No action needed", "Redelivered", "Replanned", "Returned to Shipper", "Claim handled outside the TMS"];

    [Fact]
    public async Task DraftNote_FillsTheNoteBoxFromTheTimeline_AndSavesNothing()
    {
        var (shipmentNumber, exceptionNumber) = await ShipmentWithFailedDeliveryAsync();
        await OpenListAsync(exceptionNumber);
        var saved = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.Method != "GET" && !request.Url.Contains("/_blazor", StringComparison.Ordinal))
            {
                saved.Add($"{request.Method} {request.Url}");
            }
        };

        var note = await DraftAsync(exceptionNumber);

        await Expect(note).ToHaveValueAsync(new Regex($"^Delivery failed on Shipment {Regex.Escape(shipmentNumber)}"));
        await Expect(note).ToHaveValueAsync(new Regex("Outstanding: .*The Shipper has not been notified"));
        await Expect(NotePanel(exceptionNumber)).ToContainTextAsync("nothing is saved until you do");
        await Expect(RowOf(exceptionNumber)).Not.ToContainTextAsync("Outstanding:");
        saved.Should().BeEmpty("drafting reads the timeline and posts nothing");

        // Nothing is stored: when the Planner comes back, the Exception has no note.
        await OpenListAsync(exceptionNumber);
        await Expect(RowOf(exceptionNumber)).Not.ToContainTextAsync("Outstanding:");
    }

    [Fact]
    public async Task AddNote_AfterThePlannerEditedTheDraft_SavesWhatThePlannerWrote()
    {
        var (_, exceptionNumber) = await ShipmentWithFailedDeliveryAsync();
        await OpenListAsync(exceptionNumber);
        var note = await DraftAsync(exceptionNumber);
        var edited = $"Phoned the Consignee, redelivery Thursday ({Guid.NewGuid():N})";

        await note.FillAsync(edited);
        await NotePanel(exceptionNumber).GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync(exceptionNumber);

        // This fixture slows the live list down, so the Planner opens it again to read what was stored.
        await OpenListAsync(exceptionNumber);
        var row = RowOf(exceptionNumber);
        await Expect(row).ToContainTextAsync(edited);
        await Expect(row).Not.ToContainTextAsync("Outstanding:");
    }

    [Fact]
    public async Task DraftNote_ChoosesNoOutcome_AndTheExceptionStaysOpenAfterTheNoteIsSaved()
    {
        var (_, exceptionNumber) = await ShipmentWithFailedDeliveryAsync();
        await OpenListAsync(exceptionNumber);

        var note = await DraftAsync(exceptionNumber);

        var draft = await note.InputValueAsync();
        draft.Should().Contain("The Exception stays open until the Planner resolves it.");
        draft.Should().NotContainAny(OutcomeNames, "the Planner picks the outcome, not the assistant");

        await NotePanel(exceptionNumber).GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync(exceptionNumber);

        await OpenListAsync(exceptionNumber);
        var row = RowOf(exceptionNumber);
        await Expect(row).ToContainTextAsync("Outstanding:");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = $"Resolve {exceptionNumber}" })).ToBeVisibleAsync();
    }
}
