namespace Norvia.Simulator.Domain.Runs;

/// <summary>What the presenter configures for a run. The same settings and the same seed always give the same Simulation plan.</summary>
public sealed record SimulationSettings(
    int OrderCount,
    int BadSharePercent,
    int CollectedTodayPercent,
    int Seed,
    RunMode Mode,
    TimeSpan Window,
    string? PresetName = null)
{
    public const int MaxWindowMinutes = 240;

    /// <summary>Everything wrong with these settings or the limits they break; empty when the run may go ahead.</summary>
    public IReadOnlyList<string> Problems(SimulationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var problems = new List<string>();
        if (OrderCount < 1)
        {
            problems.Add("A run needs at least one order.");
        }
        else if (OrderCount > limits.MaxOrdersPerRun)
        {
            problems.Add($"A run may hold at most {limits.MaxOrdersPerRun} orders; this one has {OrderCount}.");
        }

        if (BadSharePercent is < 0 or > 100)
        {
            problems.Add("The share of bad bookings is a percentage from 0 to 100.");
        }

        if (CollectedTodayPercent is < 0 or > 100)
        {
            problems.Add("The share of orders collected today is a percentage from 0 to 100.");
        }

        if (Mode == RunMode.Paced && (Window <= TimeSpan.Zero || Window > TimeSpan.FromMinutes(MaxWindowMinutes)))
        {
            problems.Add($"A paced run is spread over 1 to {MaxWindowMinutes} minutes.");
        }

        return problems;
    }
}
