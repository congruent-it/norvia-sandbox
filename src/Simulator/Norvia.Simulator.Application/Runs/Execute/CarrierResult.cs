namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>What the Carrier stage came to: whether the run was cancelled, how many Shipments were released, confirmed and delivered, and which were delivered.</summary>
internal sealed record CarrierResult(bool Stopped, int Released, int Confirmed, int Delivered, IReadOnlyList<CarrierDelivery> Deliveries)
{
    public static CarrierResult None { get; } = new(false, 0, 0, 0, []);
}
