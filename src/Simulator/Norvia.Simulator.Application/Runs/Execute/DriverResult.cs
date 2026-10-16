namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the Driver stage came to: whether the run was cancelled, how many Trips the Drivers started and the TMS recorded as Completed, and the Exceptions the Drivers' reports should have opened.</summary>
internal sealed record DriverResult(bool Stopped, int Started, int Completed, IReadOnlyList<ExpectedFailure> Failures)
{
    public static DriverResult None { get; } = new(false, 0, 0, []);
}
