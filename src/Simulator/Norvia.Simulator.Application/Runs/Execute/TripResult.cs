namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the Trip stage came to: whether the run was cancelled, how many Trips were built and dispatched, and which of them run today.</summary>
internal sealed record TripResult(bool Stopped, int Built, int Dispatched, IReadOnlyList<RunningTrip> Running)
{
    public static TripResult None { get; } = new(false, 0, 0, []);
}
