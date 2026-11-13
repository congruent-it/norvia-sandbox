using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The Shipper tracking assistant is limited per Persona (ADR 0049).</summary>
public sealed class AssistantLimitsCheck : Check
{
    public const string CheckId = "ASSISTANT-LIMITS";

    public override string Id => CheckId;

    public override string Rule => "The Shipper tracking assistant is limited per Persona in rate, size and daily tokens (ADR 0049).";

    public override string Title => "The tracking assistant has no limits";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "assistantLimitsOff";

    public override string Fix =>
        "Set assistantLimitsOff to false and provision the estate: the Partner portal then enforces the per-Persona rate and size limits and the daily token budgets.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.Agents?.TrackingAssistant is { LimitsOn: false }
            ? [new Evidence(TrackingAssistantFacts.AgentName, "An external user can ask without a limit on questions, size or tokens, so nothing bounds what the model costs.")]
            : [];
    }
}
