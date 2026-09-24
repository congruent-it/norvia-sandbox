namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The way a Transport Order reached the TMS intake API. The WMS handoff will reuse the same API with a channel of its
/// own, so every Transport Order records which one it came from.
/// </summary>
public enum IntakeChannel
{
    /// <summary>A Shipper's booking through the Norvia Partner API and the intake Logic App (Transport-only).</summary>
    ShipperApi = 1,
}
