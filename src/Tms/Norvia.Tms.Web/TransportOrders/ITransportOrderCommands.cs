namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// What the TMS UI changes about Transport Orders. It reads through the query side in process, but changes things only
/// through the TMS API, as any other caller does.
/// </summary>
public interface ITransportOrderCommands
{
    /// <summary>Re-rates a Transport Order whose Rating failed, typically after its Rate Card was corrected.</summary>
    Task<ReRateAnswer> ReRateAsync(string transportOrderNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Assigns one Transport Order to Own fleet or to a Carrier, which plans its one Shipment. A multi-select assigns
    /// each Transport Order with a call of its own, so each succeeds or is refused on its own.
    /// </summary>
    /// <param name="carrierId">The Carrier to carry it; null for Own fleet.</param>
    Task<AssignAnswer> AssignAsync(string transportOrderNumber, string? carrierId, CancellationToken cancellationToken);
}
