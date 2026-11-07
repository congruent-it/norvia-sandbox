using Norvia.Agents;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.CarrierInvoices.AgentView;

/// <summary>The wire names of the actions the ERP allows on a Carrier invoice line, and which Suggestion kind each one is.</summary>
public static class AgentActionNames
{
    public static IReadOnlyList<string> Of(CarrierInvoiceLineActions actions)
    {
        var names = new List<string>();
        foreach (var (flag, name, _) in Map)
        {
            if (actions.HasFlag(flag))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>Whether a Suggestion of this kind is something the ERP allows. Needs a person is always fine: it proposes nothing to do.</summary>
    public static bool Allows(CarrierInvoiceLineActions actions, SuggestedActionKind kind) =>
        kind == SuggestedActionKind.NeedsAPerson
        || Map.Any(entry => entry.Kind == kind && actions.HasFlag(entry.Flag));

    private static readonly (CarrierInvoiceLineActions Flag, string Name, SuggestedActionKind Kind)[] Map =
    [
        (CarrierInvoiceLineActions.Accept, "accept", SuggestedActionKind.Accept),
        (CarrierInvoiceLineActions.AcceptAdjusted, "accept-adjusted", SuggestedActionKind.AcceptAdjusted),
        (CarrierInvoiceLineActions.Reject, "reject", SuggestedActionKind.Reject),
        (CarrierInvoiceLineActions.Wait, "wait", SuggestedActionKind.Wait),
    ];
}
