namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the follow-up stage came to: whether the run was cancelled, and how many Re-delivery Trips were dispatched.</summary>
internal sealed record FollowUpResult(bool Stopped, int Redeliveries)
{
    public static FollowUpResult None { get; } = new(false, 0);
}
