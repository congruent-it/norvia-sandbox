namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The way a Transport Order reached the TMS intake API. The WMS handoff uses the same API with a channel of its
/// own, so every Transport Order records which one it came from.
/// </summary>
public enum IntakeChannel
{
    /// <summary>A Shipper's booking through the Norvia Partner API and the intake Logic App (Transport-only).</summary>
    ShipperApi = 1,

    /// <summary>
    /// The WMS handing over a Fulfilment Order's transport (Fulfilment). It is billed through the Fulfilment Contract, so
    /// it is never rated. No WMS calls it yet.
    /// </summary>
    Wms = 2,
}
