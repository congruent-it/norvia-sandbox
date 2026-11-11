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
    public async Task DraftNote_FillsTheNoteBoxFromTheTimeline_ChoosesNoOutcome_AndSavesNothing()
    {
        var (shipmentNumber, exceptionNumber) = await ShipmentWithFailedDeliveryAsync();
        await OpenListAsync(exceptionNumber);

        var note = await DraftAsync(exceptionNumber);

        await Expect(note).ToHaveValueAsync(new Regex($"^Delivery failed on Shipment {Regex.Escape(shipmentNumber)}"));
        (await note.InputValueAsync()).Should().NotContainAny(OutcomeNames, "the Planner picks the outcome, not the assistant");
        await Expect(NotePanel(exceptionNumber)).ToContainTextAsync("nothing is saved until you do");

        // The Planner leaves without adding the note: when they come back, the Exception has none.
        await OpenListAsync(exceptionNumber);
        await Expect(RowOf(exceptionNumber)).Not.ToContainTextAsync("Shipment " + shipmentNumber + ", carried by");
        await Expect(RowOf(exceptionNumber).GetByRole(AriaRole.Gridcell).Filter(new() { HasText = "Outstanding:" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AddNote_AfterThePlannerEditedTheDraft_SavesWhatThePlannerWrote_AndTheExceptionStaysOpen()
    {
        var (_, exceptionNumber) = await ShipmentWithFailedDeliveryAsync();
        await OpenListAsync(exceptionNumber);
        var note = await DraftAsync(exceptionNumber);
        var edited = $"Phoned the Consignee, redelivery Thursday ({Guid.NewGuid():N})";

        await note.FillAsync(edited);
        await AddNoteAndReopenAsync(exceptionNumber);

        // The list shows Open Exceptions only, and the Planner can still resolve this one.
        var row = RowOf(exceptionNumber);
        await Expect(row).ToContainTextAsync(edited);
        await Expect(row).Not.ToContainTextAsync("Outstanding:");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = $"Resolve {exceptionNumber}" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddNote_WithTheDraftUnedited_SavesTheDraft()
    {
        var (shipmentNumber, exceptionNumber) = await ShipmentWithFailedDeliveryAsync();
        await OpenListAsync(exceptionNumber);
        await DraftAsync(exceptionNumber);

        await AddNoteAndReopenAsync(exceptionNumber);

        await Expect(RowOf(exceptionNumber)).ToContainTextAsync($"Delivery failed on Shipment {shipmentNumber}");
    }
}
