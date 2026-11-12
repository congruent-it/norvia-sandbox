using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Web.ShipperTracking;

/// <summary>
/// What the panel shows of the conversation so far, kept in the Persona's own circuit and nowhere else (ADR 0049): a reload starts a new one.
/// Only an answer the Shipper was given is a turn the next question carries; a refusal or a failure is shown but not sent on.
/// </summary>
public sealed class AssistantConversation
{
    private readonly List<AssistantEntry> _entries = [];

    public IReadOnlyList<AssistantEntry> Entries => _entries;

    /// <summary>The last turns, in order, that go with the next question.</summary>
    public IReadOnlyList<AssistantTurn> History =>
        [.. _entries.Where(entry => entry.Answer.Outcome == AssistantOutcome.Answered).TakeLast(AssistantInputBuilder.MaxTurns).Select(entry => new AssistantTurn(entry.Question, entry.Answer.Text))];

    public void Add(string question, AssistantAnswer answer) => _entries.Add(new AssistantEntry(question, answer));
}
