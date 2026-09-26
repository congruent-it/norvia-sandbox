namespace Norvia.IntegrationConsole.Application.Runs.Steps;

/// <summary>How one of a step's bodies shows.</summary>
public enum BodyState
{
    /// <summary>In full.</summary>
    Shown,

    /// <summary>The step uses secure inputs or outputs: the body exists, but isn't shown.</summary>
    Hidden,

    /// <summary>Run history has no body.</summary>
    None,
}
