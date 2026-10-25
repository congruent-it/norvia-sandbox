using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The agents' model deployment has a content filter and a prompt shield.</summary>
public sealed class ContentFilterCheck : Check
{
    public const string CheckId = "AGENT-CONTENT-FILTER";

    public override string Id => CheckId;

    public override string Rule => "The agents' model deployment has a content filter and a prompt shield.";

    public override string Title => "The model has no content filter or prompt shield";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "noContentFilterOrPromptShield";

    public override string Fix =>
        "Set noContentFilterOrPromptShield to false and provision the estate: foundry.bicep then attaches the content filter policy with the prompt shield.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.Agents is { ContentFilterAndPromptShield: false }
            ? [new Evidence("model deployment", "It has no content filter or prompt shield, so nothing screens what an agent is asked or answers.")]
            : [];
    }
}
