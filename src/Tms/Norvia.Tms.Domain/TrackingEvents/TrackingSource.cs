using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>
/// Who reported a Tracking event: one named Carrier, or Norvia's Own fleet (a Driver, through P12). Naming the source in
/// the input is what lets P12 add a second caller without changing the module.
/// </summary>
public sealed record TrackingSource
{
    public static readonly TrackingSource OwnFleet = new(carrierId: null);

    private TrackingSource(CarrierId? carrierId) => CarrierId = carrierId;

    /// <summary>The Carrier that reported the event; null for Own fleet.</summary>
    public CarrierId? CarrierId { get; }

    public bool IsOwnFleet => CarrierId is null;

    public static TrackingSource Carrier(CarrierId carrierId)
    {
        ArgumentNullException.ThrowIfNull(carrierId);
        return new(carrierId);
    }

    public override string ToString() => CarrierId?.Value ?? "Own fleet";
}
