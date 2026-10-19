using Norvia.Erp.Domain.BillableLines;

namespace Norvia.Erp.Domain.ShipperInvoices;

/// <summary>
/// One line of a Shipper invoice: one Transport Order, with its charge lines as the TMS froze them. The Shipment number is kept for Finance
/// and isn't sent to the Shipper (ADR 0011).
/// </summary>
public sealed record ShipperInvoiceLine(
    string TransportOrderNumber,
    string ShipperReference,
    string ShipmentNumber,
    DateTimeOffset DeliveryTime,
    IReadOnlyList<ChargeLine> ChargeLines,
    decimal Total)
{
    public static ShipperInvoiceLine Of(BillableLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new(
            line.Id.TransportOrderNumber,
            line.ShipperReference,
            line.ShipmentNumber,
            line.DeliveryTime,
            line.FreightCharge.ChargeLines,
            line.Total);
    }
}
