using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>
/// One item of the checklist: a rule the estate's configuration must satisfy. Deterministic code evaluates it, so a Finding's
/// severity and fix are the Check's, never a model's (#371).
/// </summary>
public abstract class Check
{
    /// <summary>The Check's ID, such as <c>DLQ-ALERT</c>. Upper-case letters, digits and hyphens.</summary>
    public abstract string Id { get; }

    /// <summary>What the estate must satisfy, as a sentence.</summary>
    public abstract string Rule { get; }

    /// <summary>One line for a Finding of this Check.</summary>
    public abstract string Title { get; }

    public abstract Severity Severity { get; }

    /// <summary>The as-found toggle (#377) that breaks this rule, so every toggle has a Check that finds it.</summary>
    public abstract string AsFoundToggle { get; }

    /// <summary>The Bicep parameter or setting that puts it right.</summary>
    public abstract string Fix { get; }

    /// <summary>The resources that break the rule. Empty when the estate satisfies it.</summary>
    public abstract IReadOnlyList<Evidence> Examine(EstateConfiguration estate);
}
