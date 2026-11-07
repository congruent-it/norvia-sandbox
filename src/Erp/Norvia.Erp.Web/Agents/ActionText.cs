using Norvia.Agents;

namespace Norvia.Erp.Web.Agents;

/// <summary>How the ERP UI names the kind of action a Suggestion makes, in one place.</summary>
public static class ActionText
{
    public static string Of(SuggestedActionKind action) => action switch
    {
        SuggestedActionKind.Accept => "Accept as invoiced",
        SuggestedActionKind.AcceptAdjusted => "Accept adjusted",
        SuggestedActionKind.Reject => "Reject",
        SuggestedActionKind.Wait => "Wait",
        _ => "Needs a person",
    };
}
