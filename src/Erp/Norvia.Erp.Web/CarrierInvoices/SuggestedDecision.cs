using Norvia.Agents;
using Norvia.Erp.Web.Agents;

namespace Norvia.Erp.Web.CarrierInvoices;

/// <summary>
/// A Suggestion of the Carrier invoice matching agent that pre-selects an action on a line's decision form. It executes nothing:
/// Finance writes the remark or reason and confirms with the same button as ever. The decision names the Agent's run (ADR 0040) only
/// when Finance then does what was suggested.
/// </summary>
public sealed record SuggestedDecision(SuggestedActionKind Kind, AgentRun Run)
{
    /// <summary>The pre-selection a Suggestion makes, or null when it suggests no decision or has no run to name. Wait and Needs a person pre-select nothing.</summary>
    public static SuggestedDecision? From(Suggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        return suggestion is { Run: { } run, Action: SuggestedActionKind.Accept or SuggestedActionKind.AcceptAdjusted or SuggestedActionKind.Reject }
            ? new SuggestedDecision(suggestion.Action, run)
            : null;
    }

    /// <summary>
    /// The run to name in the trace of a decision of this kind: the Suggestion's own when Finance did what it suggested, none otherwise.
    /// Accepting the line counts as acting on a suggestion to accept it, at the invoiced amount or another: the amount is Finance's.
    /// </summary>
    public AgentRun? RunFor(SuggestedActionKind chosen) => IsAccept(chosen) && IsAccept(Kind) || chosen == Kind ? Run : null;

    /// <summary>This pre-selection when the line allows it, or null: an Accept suggestion for a line that cannot be accepted is ignored, as the ERP's own rule holds.</summary>
    public SuggestedDecision? WhenAllowed(bool canAccept) => Kind == SuggestedActionKind.Reject || canAccept ? this : null;

    private static bool IsAccept(SuggestedActionKind kind) => kind is SuggestedActionKind.Accept or SuggestedActionKind.AcceptAdjusted;

    public string Label => ActionText.Of(Kind);
}
