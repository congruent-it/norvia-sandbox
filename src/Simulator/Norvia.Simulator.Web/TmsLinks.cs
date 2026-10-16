namespace Norvia.Simulator.Web;

/// <summary>
/// Where the TMS UI is, so a log line can open the Transport Order, Shipment or Trip it names there. The TMS UI's pages are at
/// <c>/transport-orders/…</c>, <c>/shipments/…</c> and <c>/trips/…</c>.
/// </summary>
public sealed class TmsLinks(Uri? address)
{
    /// <summary>The TMS UI's page for a Transport Order, or null when the TMS UI's address isn't known.</summary>
    public string? TransportOrder(string number) => Page("transport-orders", number);

    /// <summary>The TMS UI's page for a Shipment, or null when the TMS UI's address isn't known.</summary>
    public string? Shipment(string number) => Page("shipments", number);

    /// <summary>The TMS UI's page for a Trip, or null when the TMS UI's address isn't known.</summary>
    public string? Trip(string number) => Page("trips", number);

    private string? Page(string path, string number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        return address is null ? null : $"{address.ToString().TrimEnd('/')}/{path}/{Uri.EscapeDataString(number)}";
    }
}
