using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.Planner;

/// <summary>Who carries a Shipment: Own fleet (no <see cref="CarrierId"/>) or one named Carrier.</summary>
public sealed record Assignee(string? CarrierId, string Name)
{
    public static Assignee OwnFleet { get; } = new(null, "Own fleet");

    public bool IsOwnFleet => CarrierId is null;

    public static Assignee Of(SimulatedCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        return new Assignee(carrier.Id, carrier.Name);
    }
}
