using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Configure;

/// <summary>
/// What the Configure screen offers: the Presets, the guards on a run, and a seed to start from. The hostile profile is opt-in, so the screen also lists the
/// Carriers and the Drivers it can be chosen for, one by one.
/// </summary>
public sealed record ConfigureView(IReadOnlyList<Preset> Presets, SimulationLimits Limits, int NewSeed, int NextRunNumber)
{
    /// <summary>The Carriers that can write the hostile profile: active Carriers with a Persona.</summary>
    public IReadOnlyList<HostileChoice> HostileCarriers { get; init; } = [];

    /// <summary>The Drivers of the Own fleet that can write the hostile profile.</summary>
    public IReadOnlyList<HostileChoice> HostileDrivers { get; init; } = [];
}

/// <summary>A Carrier or a Driver the hostile profile can be chosen for.</summary>
public sealed record HostileChoice(string Id, string Name);
