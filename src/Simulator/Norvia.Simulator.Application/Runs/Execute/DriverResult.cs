namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the Driver stage came to: whether the run was cancelled, and how many Trips the Drivers started and the TMS recorded as Completed.</summary>
internal sealed record DriverResult(bool Stopped, int Started, int Completed)
{
    public static DriverResult None { get; } = new(false, 0, 0);
}
