namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the Carrier stage came to: whether the run was cancelled, and how many Shipments were released, confirmed and delivered.</summary>
internal sealed record CarrierResult(bool Stopped, int Released, int Confirmed, int Delivered)
{
    public static CarrierResult None { get; } = new(false, 0, 0, 0);
}
