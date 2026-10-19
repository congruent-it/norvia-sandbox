namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// A Shipper Persona's invoices inbox in the Partner portal, which the Shipper invoice Logic App puts the invoices and Credit notes in. Reading it is how the
/// Simulator sees a document arrive.
/// </summary>
public interface IShipperInbox
{
    /// <summary>What the inbox holds, or null when it couldn't be read (the portal unreachable, or no credential for the Shipper).</summary>
    Task<IReadOnlyList<InboxDocument>?> ListAsync(string shipperId, CancellationToken cancellationToken);
}
