using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The nightly conversation sweeper runs.</summary>
public sealed class ConversationSweeperCheck : Check
{
    public const string CheckId = "CONVERSATION-SWEEPER";

    public override string Id => CheckId;

    public override string Rule => "The nightly conversation sweeper runs.";

    public override string Title => "The conversation sweeper does not run";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "noConversationSweeper";

    public override string Fix =>
        "Set noConversationSweeper to false and provision the estate: the conversation-sweeper Logic App is then enabled.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.ConversationSweeperRuns == false
            ? [new Evidence("conversation-sweeper", "The Logic App is stopped, so a conversation the host could not delete stays in the Foundry project, and no failed run says so.")]
            : [];
    }
}
