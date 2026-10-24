using Norvia.Agents;

namespace Norvia.IntegrationConsole.Web.Agents;

/// <summary>How the console words a Suggestion's kind of action.</summary>
internal static class SuggestionText
{
    public static string Action(SuggestedActionKind action) => action switch
    {
        SuggestedActionKind.Resubmit => "Resubmit",
        SuggestedActionKind.Discard => "Discard",
        SuggestedActionKind.FixTheCauseFirst => "Fix the cause first",
        _ => "Needs a person",
    };
}
