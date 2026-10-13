using System.ComponentModel.DataAnnotations;

namespace Norvia.Simulator.Infrastructure;

/// <summary>The Simulator's switch and guards. It is off unless the estate turns it on.</summary>
public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    /// <summary>Whether the Simulator does anything. The AppHost turns it on locally; the estate decides in Azure.</summary>
    public bool Enabled { get; set; }

    /// <summary>The hard cap on the orders of a run (about 1,000), which a local run lowers.</summary>
    [Range(1, 10_000)]
    public int MaxOrdersPerRun { get; set; } = 1000;

    /// <summary>The cap on requests in flight to the Partner API.</summary>
    [Range(1, 64)]
    public int MaxConcurrentRequests { get; set; } = 8;
}
