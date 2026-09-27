namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// What the TMS UI changes about Transport Orders. It reads through the query side in process, but changes things only
/// through the TMS API, as any other caller does.
/// </summary>
public interface ITransportOrderCommands
{
    /// <summary>Re-rates a Transport Order whose Rating failed, typically after its Rate Card was corrected.</summary>
    Task<ReRateAnswer> ReRateAsync(string transportOrderNumber, CancellationToken cancellationToken);
}
