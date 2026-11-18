namespace Norvia.Simulator.TestSupport.Ui;

/// <summary>The extra Simulators a UI test project's journeys need, beside the first one. A project names them with <see cref="SimulatorVariantsAttribute"/>, so it starts only those.</summary>
public static class SimulatorVariants
{
    /// <summary>Its Own fleet is another Vehicle and Driver, so a Trip of its run is its own on a day.</summary>
    public const string Signatures = "simulator-signatures";

    /// <summary>The Agents are on, with the scripted model.</summary>
    public const string Agents = "simulator-agents";

    /// <summary>The Agents are on and the Own fleet is the Driver texts journey's.</summary>
    public const string AgentsWithDriverFleet = "simulator-agents-drivers";

    /// <summary>The Agents are on and the model cannot write the terse Dutch pools.</summary>
    public const string FailingAgent = "simulator-agents-failing";

    /// <summary>The operator holds only the Read role.</summary>
    public const string Viewer = "simulator-viewer";

    /// <summary>The TMS API says its reset is switched off.</summary>
    public const string ResetSwitchedOff = "simulator-reset-off";

    /// <summary>The TMS API refuses the reset.</summary>
    public const string ResetRefused = "simulator-reset-refused";
}

/// <summary>Names the extra Simulators the test assembly's <see cref="SimulatorUiFixture"/> starts.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class SimulatorVariantsAttribute(params string[] names) : Attribute
{
    public IReadOnlyList<string> Names { get; } = names;
}
