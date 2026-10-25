using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>Agents:Enabled is off in a Demo UI the public can reach.</summary>
public sealed class AgentsEnabledInPublicDemoUiCheck : Check
{
    public const string CheckId = "AGENTS-PUBLIC";

    public override string Id => CheckId;

    public override string Rule => "Agents:Enabled is off in a Demo UI the public can reach.";

    public override string Title => "Agents are on in a public Demo UI";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "agentsEnabledInPublicDemoUi";

    public override string Fix =>
        "Set agentsEnabledInPublicDemoUi to false and provision the estate: Agents:Enabled is then off in the Demo UIs.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.Agents is { EnabledInPublicDemoUi: true }
            ? [new Evidence("Demo UI", "Agents:Enabled is on in a UI the public can reach, so anyone can spend the model's budget.")]
            : [];
    }
}
