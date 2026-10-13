namespace Norvia.Simulator.Domain.Runs;

/// <summary>A named set of numbers that fills the Configure screen, such as "Quiet Monday".</summary>
public sealed record Preset(
    string Name,
    string Description,
    int OrderCount,
    int BadSharePercent,
    int CollectedTodayPercent,
    RunMode Mode,
    TimeSpan Window)
{
    /// <summary>The settings this Preset fills in, with the seed the presenter chose.</summary>
    public SimulationSettings ToSettings(int seed) =>
        new(OrderCount, BadSharePercent, CollectedTodayPercent, seed, Mode, Window, Name);
}
