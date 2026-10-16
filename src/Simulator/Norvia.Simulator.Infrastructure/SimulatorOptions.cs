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

    /// <summary>How long the Planner waits between asking the TMS for the Exceptions the Carriers' tracking opens: Norvia processes tracking after it accepts it.</summary>
    [Range(0, 60)]
    public int ExceptionPollSeconds { get; set; } = 3;

    /// <summary>How many times the Planner asks for them before it notes that an Exception didn't open.</summary>
    [Range(1, 100)]
    public int ExceptionPollAttempts { get; set; } = 10;

    /// <summary>How long a Driver waits between asking the Driver app whether a Trip has arrived, or the TMS has recorded their report: the Logic Apps carry both.</summary>
    [Range(0, 60)]
    public int DriverPollSeconds { get; set; } = 2;

    /// <summary>How many times a Driver asks before they note that the estate didn't catch up.</summary>
    [Range(1, 300)]
    public int DriverPollAttempts { get; set; } = 60;
}
