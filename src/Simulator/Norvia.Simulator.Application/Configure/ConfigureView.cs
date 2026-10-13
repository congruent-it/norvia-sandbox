using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Configure;

/// <summary>What the Configure screen offers: the Presets, the guards on a run, and a seed to start from.</summary>
public sealed record ConfigureView(IReadOnlyList<Preset> Presets, SimulationLimits Limits, int NewSeed, int NextRunNumber);
